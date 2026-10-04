using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.D3D11;
using SpectraShade.Compiler;
using System;
using System.Diagnostics;
using System.Numerics;
using Xunit;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The D3D11 shared present target, against a real driver: the NT handle, the
/// keyed mutex, and the view format that stops the picture being encoded twice.
/// </summary>
// The consumer is a second plain D3D11 device, no compositor. Failures here
// raise nothing on the debug layer, so everything is asserted with bytes.
[Collection(D3DDeviceCollection.Name)]
public sealed unsafe class SharedTargetD3D11Tests(SharedTargetD3D11Fixture fixture)
{
    private void Require() => Assert.SkipWhen(
        !fixture.Available,
        $"no usable D3D11 device in this process: {fixture.UnavailableReason}");

    [Fact]
    public void A_composited_surface_brings_a_device_up_with_no_swap_chain()
    {
        Require();

        fixture.Renderer.CurrentPipelineName.ShouldNotBe("None");
        fixture.Renderer.DebugLayerErrorCount.ShouldBe(0);
    }

    [Fact]
    public void A_composited_surface_hands_out_an_nt_handle_with_its_size_and_generation()
    {
        Require();

        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();

        handle.NtHandle.ShouldNotBe(0);
        handle.Width.ShouldBe(SharedTargetD3D11Fixture.Width);
        handle.Height.ShouldBe(SharedTargetD3D11Fixture.Height);
        handle.Generation.ShouldBeGreaterThan(0, "zero is reserved for no target at all");
    }

    [Fact]
    public void The_handle_opens_on_a_second_device_that_knows_nothing_about_this_renderer()
    {
        Require();
        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();

        using var consumer = new ConsumerDevice(handle.NtHandle);

        consumer.Width.ShouldBe((uint)handle.Width);
        consumer.Height.ShouldBe((uint)handle.Height);
    }

    [Fact]
    public void The_imported_resource_is_unorm_so_the_engines_srgb_write_is_not_encoded_twice()
    {
        Require();
        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();

        using var consumer = new ConsumerDevice(handle.NtHandle);

        consumer.Format.ShouldBe(
            Format.FormatR8G8B8A8Unorm,
            "the shared RESOURCE must not be sRGB-typed; only the render-target view over it is");
    }

    [Fact]
    public void The_producer_and_the_consumer_take_turns_on_keys_zero_and_one()
    {
        Require();
        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();

        using var consumer = new ConsumerDevice(handle.NtHandle);

        fixture.Renderer.BeginSharedWrite(1000).ShouldBeTrue();
        fixture.Renderer.EndSharedWrite();

        consumer.Acquire(Renderer.SharedConsumerKey, 1000).ShouldBe(0);
        consumer.Release(Renderer.SharedProducerKey).ShouldBe(0);

        // Twice: releasing the wrong key works once and deadlocks on the second turn.
        fixture.Renderer.BeginSharedWrite(1000).ShouldBeTrue();
        fixture.Renderer.EndSharedWrite();
        consumer.Acquire(Renderer.SharedConsumerKey, 1000).ShouldBe(0);
        consumer.Release(Renderer.SharedProducerKey).ShouldBe(0);
    }

    [Fact]
    public void A_turn_the_consumer_never_takes_times_out_instead_of_blocking_the_render_thread()
    {
        Require();
        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();

        using var consumer = new ConsumerDevice(handle.NtHandle);

        fixture.Renderer.BeginSharedWrite(1000).ShouldBeTrue();
        fixture.Renderer.EndSharedWrite();
        consumer.Acquire(Renderer.SharedConsumerKey, 1000).ShouldBe(0);

        try
        {
            var waited = Stopwatch.StartNew();
            fixture.Renderer.BeginSharedWrite(50).ShouldBeFalse();
            waited.Stop();

            // Generous: only rules out a wait that never returns.
            waited.ElapsedMilliseconds.ShouldBeLessThan(2000);
        }
        finally
        {
            consumer.Release(Renderer.SharedProducerKey).ShouldBe(0);
        }

        // The next frame still goes through.
        fixture.Renderer.BeginSharedWrite(1000).ShouldBeTrue();
        fixture.Renderer.EndSharedWrite();
        consumer.Acquire(Renderer.SharedConsumerKey, 1000).ShouldBe(0);
        consumer.Release(Renderer.SharedProducerKey).ShouldBe(0);
    }

    [Fact]
    public void A_clear_written_under_the_key_arrives_on_the_other_device_encoded_once()
    {
        // Linear 0.5 encodes to sRGB 188 of 255. 128 means the view never encoded.
        Require();
        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();

        using var consumer = new ConsumerDevice(handle.NtHandle);

        int errorsBefore = fixture.Renderer.DebugLayerErrorCount;

        fixture.Renderer.BeginSharedWrite(1000).ShouldBeTrue();
        try
        {
            fixture.Renderer.BeginPass(
                fixture.Renderer.PresentTargetForTest,
                PassClear.To(new Vector4(0.5f, 0f, 1f, 1f)));
            fixture.Renderer.EndPass();
        }
        finally
        {
            fixture.Renderer.EndSharedWrite();
        }

        (byte r, byte g, byte b, byte a) = consumer.ReadFirstPixel();

        r.ShouldBeInRange((byte)184, (byte)192, "128 would mean the sRGB render-target view never encoded at all");
        g.ShouldBe((byte)0);
        b.ShouldBe((byte)255);
        a.ShouldBe((byte)255);

        fixture.Renderer.DebugLayerErrorCount.ShouldBe(errorsBefore);
    }

    [Fact]
    public void Writing_without_the_key_silently_writes_nothing()
    {
        // Why the write bracket exists: a keyless clear returns S_OK, the debug
        // layer says nothing, and the texture does not change.
        Require();
        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();

        using var consumer = new ConsumerDevice(handle.NtHandle);

        fixture.Renderer.BeginSharedWrite(1000).ShouldBeTrue();
        try
        {
            fixture.Renderer.BeginPass(fixture.Renderer.PresentTargetForTest, PassClear.To(new Vector4(0f, 1f, 0f, 1f)));
            fixture.Renderer.EndPass();
        }
        finally
        {
            fixture.Renderer.EndSharedWrite();
        }

        // Different colour, no key.
        int errorsBefore = fixture.Renderer.DebugLayerErrorCount;
        fixture.Renderer.BeginPass(fixture.Renderer.PresentTargetForTest, PassClear.To(new Vector4(1f, 0f, 0f, 1f)));
        fixture.Renderer.EndPass();

        (byte r, byte g, byte b, _) = consumer.ReadFirstPixel();

        g.ShouldBe((byte)255, "the keyless clear must not have landed");
        r.ShouldBe((byte)0);
        b.ShouldBe((byte)0);
        fixture.Renderer.DebugLayerErrorCount.ShouldBe(errorsBefore, "and it raises nothing, which is the whole problem");
    }

    [Fact]
    public void A_whole_target_readback_agrees_with_the_one_texel_form_about_which_way_up_it_is()
    {
        // Odd size on both axes: 37 texels is 148 bytes a row, a multiple of no
        // D3D alignment, so a staging surface that pads its rows gets exercised.
        Require();

        const int Width = 37;
        const int Height = 23;

        RenderTarget target = fixture.Renderer.CreateRenderTarget(new RenderTargetDesc(
            Width, Height, TextureFormat.Rgba8, TextureColorSpace.Linear, Depth: false));
        Texture white = fixture.Renderer.CreateTexture(
            [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);
        try
        {
            fixture.Renderer.DrawOrientationQuad(white, target, OrientationQuad.Coverage.TopHalf);

            var picture = new byte[Width * Height * 4];
            fixture.Renderer.ReadTargetPixels(target, picture);

            // Destination row 0 is the bottom of the picture.
            picture[0].ShouldBeLessThan((byte)80, "the destination's first row must be the bottom of the picture");
            picture[((Height - 1) * Width * 4)].ShouldBeGreaterThan((byte)120);

            // Texel for texel against the single-pixel read, to catch a wrong row pitch.
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    (byte r, byte g, byte b, byte a) = fixture.Renderer.ReadTargetPixel(target, x, y);
                    int offset = ((y * Width) + x) * 4;
                    picture[offset].ShouldBe(r, $"red at ({x}, {y})");
                    picture[offset + 1].ShouldBe(g, $"green at ({x}, {y})");
                    picture[offset + 2].ShouldBe(b, $"blue at ({x}, {y})");
                    picture[offset + 3].ShouldBe(a, $"alpha at ({x}, {y})");
                }
            }
        }
        finally
        {
            fixture.Renderer.DestroyTexture(white);
            fixture.Renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void The_shared_route_and_an_ordinary_srgb_target_encode_a_colour_the_same_way()
    {
        Require();

        var linear = new Vector4(0.5f, 0.25f, 0.75f, 1f);
        RenderTarget reference = CreateReferenceTarget();
        try
        {
            fixture.Renderer.ClearForTest(reference, linear);
            WriteSharedTarget(linear);

            ViewportCompare.Reading reading = CompareSharedAgainst(reference);

            reading.MaxDelta.ShouldBeLessThanOrEqualTo(ViewportCompare.Threshold, reading.ToString());
            reading.Passes.ShouldBeTrue(reading.ToString());
        }
        finally
        {
            fixture.Renderer.DestroyRenderTarget(reference);
        }
    }

    [Fact]
    public void A_double_encode_on_the_shared_route_is_caught_rather_than_absorbed()
    {
        // The shared target is written an already-encoded value, so its sRGB
        // view encodes it a second time.
        Require();

        var linear = new Vector4(0.5f, 0.25f, 0.75f, 1f);
        Vector3 alreadyEncoded = ColorSpace.LinearToSrgb(new Vector3(linear.X, linear.Y, linear.Z));

        RenderTarget reference = CreateReferenceTarget();
        try
        {
            fixture.Renderer.ClearForTest(reference, linear);
            WriteSharedTarget(new Vector4(alreadyEncoded, 1f));

            ViewportCompare.Reading reading = CompareSharedAgainst(reference);

            reading.Passes.ShouldBeFalse(
                "a transfer function applied twice must not be inside the tolerance: " + reading);
            reading.MaxDelta.ShouldBeGreaterThan(
                ViewportCompare.Threshold * 10,
                "the failure this guards is tens of levels, not a rounding difference: " + reading);
        }
        finally
        {
            fixture.Renderer.DestroyRenderTarget(reference);
        }
    }

    // Same format as the window's back buffer on this backend.
    private RenderTarget CreateReferenceTarget() => fixture.Renderer.CreateRenderTarget(new RenderTargetDesc(
        SharedTargetD3D11Fixture.Width, SharedTargetD3D11Fixture.Height,
        TextureFormat.Rgba8, TextureColorSpace.Srgb, Depth: false));

    private void WriteSharedTarget(Vector4 color)
    {
        fixture.Renderer.BeginSharedWrite(1000).ShouldBeTrue();
        try
        {
            fixture.Renderer.ClearForTest(
                fixture.Renderer.PresentTargetForTest.ShouldNotBeNull(), color);
        }
        finally
        {
            fixture.Renderer.EndSharedWrite();
        }
    }

    // The shared read takes the consumer's turn and hands key 0 back, which
    // later tests rely on.
    private ViewportCompare.Reading CompareSharedAgainst(RenderTarget reference)
    {
        var window = new byte[reference.Width * reference.Height * 4];
        fixture.Renderer.ReadTargetPixels(reference, window);

        var shared = new byte[window.Length];
        fixture.Renderer.TryReadSharedPixels(shared, 1000)
            .ShouldBeTrue("the shared target's key never came back");

        return ViewportCompare.Compare(window, shared);
    }

    [Fact]
    public void A_shared_target_refuses_to_be_resized_in_place()
    {
        // The consumer imported the handle, so the resource cannot be swapped
        // under it. A resize recreates the target under a new generation.
        Require();

        RenderTarget target = fixture.Renderer.PresentTargetForTest.ShouldNotBeNull();

        Should.Throw<InvalidOperationException>(() => target.Resize(target.Width + 16, target.Height + 16))
            .Message.ShouldContain("generation");
    }

    [Fact]
    public void A_resize_mints_a_new_generation_and_a_new_handle()
    {
        Require();

        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle before).ShouldBeTrue();
        try
        {
            fixture.Resize(SharedTargetD3D11Fixture.Width + 32, SharedTargetD3D11Fixture.Height + 16);

            fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle after).ShouldBeTrue();

            after.Generation.ShouldBeGreaterThan(before.Generation);
            after.NtHandle.ShouldNotBe(before.NtHandle);
            after.Width.ShouldBe(SharedTargetD3D11Fixture.Width + 32);
            after.Height.ShouldBe(SharedTargetD3D11Fixture.Height + 16);

            // The retired target is held until the consumer acknowledges it.
            Should.NotThrow(() => fixture.Renderer.NotifySharedTargetReleased(before.Generation));
            fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle still).ShouldBeTrue();
            still.ShouldBe(after);
        }
        finally
        {
            // Back to the size the other tests expect.
            fixture.Resize(SharedTargetD3D11Fixture.Width, SharedTargetD3D11Fixture.Height);
            fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle restored);
            fixture.Renderer.NotifySharedTargetReleased(restored.Generation - 1);
        }

        fixture.Renderer.DebugLayerErrorCount.ShouldBe(0);
    }
}

/// <summary>Serialises every test class in this assembly that brings up a D3D device.</summary>
// Two reasons. The shared-target tests take turns on one keyed mutex. And
// classes acquiring Silk.NET's D3D and D3DCompiler APIs concurrently race:
// D3D11CreateDevice reports success with a null device pointer.
// D3D12 stays in this collection too. The race is in the API tables, so a
// separate D3D12 collection brings it back.
[CollectionDefinition(Name)]
public sealed class D3DDeviceCollection
    : ICollectionFixture<SharedTargetD3D11Fixture>, ICollectionFixture<SharedTargetD3D12Fixture>
{
    public const string Name = "D3D device";
}

/// <summary>
/// A <see cref="D3D11Renderer"/> initialized against a composited surface, or a
/// recorded reason why not.
/// </summary>
// Tests skip on a machine with no D3D11 driver. Availability is probed with a
// throwaway device and nothing after that is caught: a blanket catch would
// report a real renderer bug as "no device" and turn it into skips.
public sealed unsafe class SharedTargetD3D11Fixture : IDisposable
{
    public const int Width = 64;

    public const int Height = 48;

    private readonly D3D11Renderer? _renderer;

    public SharedTargetD3D11Fixture()
    {
        if (!DeviceIsAvailable(out string reason))
        {
            UnavailableReason = reason;
            Available = false;
            return;
        }

        var renderer = new D3D11Renderer(NullLogger<Renderer>.Instance, new SpectraShadeCompiler());

        // A composited surface has no swap chain to ask, so this is the only size.
        renderer.SetFramebufferSize(new Vector2D<int>(Width, Height));
        renderer.Initialize(new CompositedSurface(Width, Height));

        _renderer = renderer;
        Available = true;
        UnavailableReason = string.Empty;
    }

    private static bool DeviceIsAvailable(out string reason)
    {
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;

        int hr = Silk.NET.Direct3D11.D3D11.GetApi(null).CreateDevice(
            (IDXGIAdapter*)null, D3DDriverType.Hardware, (nint)0, 0u,
            (D3DFeatureLevel*)null, 0u, Silk.NET.Direct3D11.D3D11.SdkVersion,
            &device, null, &context);

        if (context is not null) context->Release();
        if (device is not null) device->Release();

        reason = hr < 0 || device is null
            ? $"D3D11CreateDevice returned 0x{hr:X8}"
            : string.Empty;
        return reason.Length == 0;
    }

    public bool Available { get; }

    public string UnavailableReason { get; }

    public D3D11Renderer Renderer => _renderer
        ?? throw new InvalidOperationException("No D3D11 device; the test should have skipped.");

    /// <summary>Resizes the present target the way a host resize does.</summary>
    public void Resize(int width, int height)
    {
        Renderer.SetFramebufferSize(new Vector2D<int>(width, height));

        // The engine picks a resize up by rendering a frame, which needs a
        // scene. This runs the target maintenance alone.
        Renderer.EnsurePresentTargetForTest();
    }

    public void Dispose() => _renderer?.Shutdown();

    private sealed class CompositedSurface(int width, int height) : IRenderSurface
    {
        public RenderSurfaceKind Kind => RenderSurfaceKind.Composited;
        public nint NativeHandle => 0;
        public IGLContext? GLContext => null;
        public Vector2D<int> PixelSize => new(width, height);

        public event Action<Vector2D<int>>? Resized
        {
            add { }
            remove { }
        }
    }
}

// A second plain D3D11 device that imports the engine's handle and takes its
// turn on the keyed mutex. Stands in for the compositor.
internal sealed unsafe class ConsumerDevice : IDisposable
{
    private readonly Silk.NET.Direct3D11.D3D11 _api = Silk.NET.Direct3D11.D3D11.GetApi();
    private ComPtr<ID3D11Device> _device;
    private ComPtr<ID3D11DeviceContext> _context;
    private ComPtr<ID3D11Texture2D> _opened;
    private ComPtr<IDXGIKeyedMutex> _mutex;

    internal ConsumerDevice(nint sharedHandle)
    {
        D3DFeatureLevel* levels = stackalloc D3DFeatureLevel[1] { D3DFeatureLevel.Level110 };
        D3DFeatureLevel chosen = default;
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;

        // Default adapter, same as the renderer. A shared handle only opens on
        // the adapter that created it.
        SilkMarshal.ThrowHResult(_api.CreateDevice(
            (IDXGIAdapter*)null, D3DDriverType.Hardware, 0, (uint)CreateDeviceFlag.BgraSupport,
            levels, 1, Silk.NET.Direct3D11.D3D11.SdkVersion, &device, &chosen, &context));
        _device = Own(device);
        _context = Own(context);

        // OpenSharedResource1 takes an NT handle. The legacy OpenSharedResource
        // wants a global handle this texture does not have.
        ID3D11Device1* device1 = null;
        Guid device1Guid = ID3D11Device1.Guid;
        SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->QueryInterface(&device1Guid, (void**)&device1));
        ComPtr<ID3D11Device1> asDevice1 = Own(device1);
        try
        {
            ID3D11Texture2D* opened = null;
            Guid textureGuid = ID3D11Texture2D.Guid;
            SilkMarshal.ThrowHResult(((ID3D11Device1*)asDevice1.Handle)->OpenSharedResource1(
                (void*)sharedHandle, &textureGuid, (void**)&opened));
            _opened = Own(opened);
        }
        finally
        {
            Release(ref asDevice1);
        }

        Texture2DDesc desc = default;
        ((ID3D11Texture2D*)_opened.Handle)->GetDesc(&desc);
        Width = desc.Width;
        Height = desc.Height;
        Format = desc.Format;

        IDXGIKeyedMutex* mutex = null;
        Guid mutexGuid = IDXGIKeyedMutex.Guid;
        SilkMarshal.ThrowHResult(((ID3D11Texture2D*)_opened.Handle)->QueryInterface(&mutexGuid, (void**)&mutex));
        _mutex = Own(mutex);
    }

    internal uint Width { get; }

    internal uint Height { get; }

    // The resource's format, not a view's.
    internal Format Format { get; }

    internal int Acquire(ulong key, uint timeoutMs) =>
        ((IDXGIKeyedMutex*)_mutex.Handle)->AcquireSync(key, timeoutMs);

    internal int Release(ulong key) => ((IDXGIKeyedMutex*)_mutex.Handle)->ReleaseSync(key);

    // Reads texel (0, 0) through a staging copy: a shared texture cannot be mapped.
    internal (byte R, byte G, byte B, byte A) ReadFirstPixel()
    {
        SilkMarshal.ThrowHResult(Acquire(Renderer.SharedConsumerKey, 1000));
        try
        {
            var desc = new Texture2DDesc
            {
                Width = Width,
                Height = Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format,
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Staging,
                BindFlags = 0,
                CPUAccessFlags = (uint)CpuAccessFlag.Read,
                MiscFlags = 0,
            };

            ID3D11Texture2D* staging = null;
            SilkMarshal.ThrowHResult(((ID3D11Device*)_device.Handle)->CreateTexture2D(&desc, null, &staging));
            ComPtr<ID3D11Texture2D> owned = Own(staging);
            try
            {
                var ctx = (ID3D11DeviceContext*)_context.Handle;
                ctx->CopyResource((ID3D11Resource*)owned.Handle, (ID3D11Resource*)_opened.Handle);

                MappedSubresource mapped = default;
                SilkMarshal.ThrowHResult(ctx->Map((ID3D11Resource*)owned.Handle, 0, Map.Read, 0, &mapped));
                byte* p = (byte*)mapped.PData;
                var pixel = (p[0], p[1], p[2], p[3]);
                ctx->Unmap((ID3D11Resource*)owned.Handle, 0);
                return pixel;
            }
            finally
            {
                Release(ref owned);
            }
        }
        finally
        {
            SilkMarshal.ThrowHResult(Release(Renderer.SharedProducerKey));
        }
    }

    public void Dispose()
    {
        Release(ref _mutex);
        Release(ref _opened);
        Release(ref _context);
        Release(ref _device);
    }

    // Silk's ComPtr constructor AddRefs, so a pointer from Create* or
    // QueryInterface needs one Release after wrapping.
    private static ComPtr<T> Own<T>(T* raw) where T : unmanaged, IComVtbl<T>
    {
        if (raw is null) return default;
        var owned = new ComPtr<T>(raw);
        ((IUnknown*)raw)->Release();
        return owned;
    }

    private static void Release<T>(ref ComPtr<T> field) where T : unmanaged, IComVtbl<T>
    {
        if (field.Handle is null) return;
        field.Dispose();
        field = default;
    }
}
