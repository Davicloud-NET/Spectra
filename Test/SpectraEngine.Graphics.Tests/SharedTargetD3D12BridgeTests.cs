using Microsoft.Extensions.Logging;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.D3D12;
using SpectraShade.Compiler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Xunit;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The D3D12 composited present path, against a real driver: the D3D11On12
/// bridge, the keyed mutex on the texture it owns, and the debug layer staying
/// silent across the wrapped-resource bracket.
/// </summary>
// The compositor refuses a D3D12-created handle (E_NOINTERFACE), so the frame
// lands in a private target and one copy per frame carries it across the bridge.
// A wrong resource state on the wrapped resource shows up only on the debug
// layer, hence the error-count assertions.
[Collection(D3DDeviceCollection.Name)]
public sealed unsafe class SharedTargetD3D12BridgeTests(SharedTargetD3D12Fixture fixture)
{
    [Fact]
    public void Completed_mesh_pool_obeys_global_bucket_and_idle_limits()
    {
        Require();
        var renderer = fixture.Renderer;
        var buffers = new List<(uint Size, ComPtr<ID3D12Resource> Resource)>();
        try
        {
            // Five full buckets exceed the global limit while each fits its own.
            // Rent all before returning any, so every resource is distinct.
            for (uint size = 1024 * 1024; size <= 16 * 1024 * 1024; size *= 2)
                for (int i = 0; i < 16 * 1024 * 1024 / size; i++)
                    buffers.Add((size, renderer.RentMeshBuffer(size)));
        }
        finally
        {
            foreach (var entry in buffers) renderer.ReturnMeshBuffer(entry.Size, entry.Resource);
        }
        renderer.WaitForGpu();
        fixture.Present();
        renderer.MeshBufferMemory.Retired.ShouldBe(0UL);
        renderer.MeshBufferMemory.Pooled.ShouldBeLessThanOrEqualTo(D3D12Renderer.MeshPoolLimit);
        // Idle buffers expire even if no mesh asks for them.
        var target = renderer.CreateRenderTarget(new RenderTargetDesc(8, 8));
        try
        {
            for (int i = 0; i < 301; i++) renderer.ClearForTest(target, Vector4.Zero);
        }
        finally { renderer.DestroyRenderTarget(target); }
        fixture.Present();
        renderer.MeshBufferMemory.Pooled.ShouldBe(0UL);
        renderer.DebugLayerErrorCount.ShouldBe(0, fixture.Diagnostics);
    }
    private void Require() => Assert.SkipWhen(
        !fixture.Available,
        $"no usable D3D12 device in this process: {fixture.UnavailableReason}");

    [Fact]
    public void A_composited_surface_brings_a_device_up_with_no_swap_chain()
    {
        Require();

        fixture.Renderer.CurrentPipelineName.ShouldNotBe("None");
        fixture.Renderer.DebugLayerErrorCount.ShouldBe(0, fixture.Diagnostics);
    }

    [Fact]
    public void The_bridge_hands_out_an_nt_handle_with_its_size_and_generation()
    {
        Require();

        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();

        handle.NtHandle.ShouldNotBe(0);
        handle.Width.ShouldBe(SharedTargetD3D12Fixture.Width);
        handle.Height.ShouldBe(SharedTargetD3D12Fixture.Height);
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
            "the shared RESOURCE must not be sRGB-typed; only the views over it are");
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
        // AcquireSync times out with WAIT_TIMEOUT (0x102), a positive HRESULT,
        // so an `hr < 0` check reads a stalled consumer as success.
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
    public void A_frame_published_through_the_bridge_arrives_on_the_other_device_encoded_once()
    {
        // Linear 0.5 encodes to sRGB 188 of 255. 128 means the view never
        // encoded, 0 means the copy never landed.
        Require();
        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();

        using var consumer = new ConsumerDevice(handle.NtHandle);

        int errorsBefore = fixture.Renderer.DebugLayerErrorCount;

        fixture.WriteAndPublish(new Vector4(0.5f, 0f, 1f, 1f));

        (byte r, byte g, byte b, byte a) = consumer.ReadFirstPixel();

        r.ShouldBeInRange((byte)184, (byte)192, "128 would mean the sRGB render-target view never encoded at all");
        g.ShouldBe((byte)0);
        b.ShouldBe((byte)255);
        a.ShouldBe((byte)255);

        fixture.Renderer.DebugLayerErrorCount.ShouldBe(errorsBefore, fixture.Diagnostics);
    }

    [Fact]
    public void The_debug_layer_stays_silent_across_a_bridged_frame()
    {
        Require();
        Assert.SkipWhen(
            !fixture.Renderer.DebugLayerActive,
            "the D3D12 validation layer is not running, so a zero error count proves nothing");

        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();
        using var consumer = new ConsumerDevice(handle.NtHandle);

        int errorsBefore = fixture.Renderer.DebugLayerErrorCount;

        // Several frames: a release into the wrong state only fails on the
        // frame after. The consumer takes its turn between them, or the
        // producer skips every publish after the first.
        for (int i = 0; i < 4; i++)
        {
            fixture.WriteAndPublish(new Vector4(0f, 1f, 0f, 1f));
            TakeTurn(consumer);
        }

        fixture.Renderer.DebugLayerErrorCount.ShouldBe(
            errorsBefore,
            "a wrapped resource acquired from a state it is not in is reported here and nowhere else: "
                + fixture.Diagnostics);
    }

    [Fact]
    public void A_resize_mints_a_new_generation_and_a_new_handle()
    {
        Require();

        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle before).ShouldBeTrue();
        try
        {
            fixture.Resize(SharedTargetD3D12Fixture.Width + 32, SharedTargetD3D12Fixture.Height + 16);

            fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle after).ShouldBeTrue();

            after.Generation.ShouldBeGreaterThan(before.Generation);
            after.NtHandle.ShouldNotBe(before.NtHandle);
            after.Width.ShouldBe(SharedTargetD3D12Fixture.Width + 32);
            after.Height.ShouldBe(SharedTargetD3D12Fixture.Height + 16);

            // The retired pair is held until the consumer acknowledges it.
            Should.NotThrow(() => fixture.Renderer.NotifySharedTargetReleased(before.Generation));
            fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle still).ShouldBeTrue();
            still.ShouldBe(after);

            // The bridge must have rebuilt its wrap for the new target.
            using var consumer = new ConsumerDevice(after.NtHandle);
            fixture.WriteAndPublish(new Vector4(0f, 1f, 0f, 1f));
            (byte r, byte g, byte b, _) = consumer.ReadFirstPixel();
            g.ShouldBe((byte)255);
            r.ShouldBe((byte)0);
            b.ShouldBe((byte)0);
        }
        finally
        {
            // Back to the size the other tests expect.
            fixture.Resize(SharedTargetD3D12Fixture.Width, SharedTargetD3D12Fixture.Height);
            fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle restored);
            fixture.Renderer.NotifySharedTargetReleased(restored.Generation - 1);
        }

        fixture.Renderer.DebugLayerErrorCount.ShouldBe(0, fixture.Diagnostics);
    }

    [Fact]
    public void A_shared_target_write_needs_no_swap_chain_to_end_its_frame()
    {
        // With no chain to present, the frame end must still wait for the GPU:
        // the upload ring, the mesh buffer pool and the descriptor rings all
        // assume it is idle there.
        Require();
        fixture.Renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();
        using var consumer = new ConsumerDevice(handle.NtHandle);

        int errorsBefore = fixture.Renderer.DebugLayerErrorCount;

        for (int i = 0; i < 4; i++)
        {
            fixture.WriteAndPublish(new Vector4(0f, 0f, 0f, 1f));
            TakeTurn(consumer);
        }

        fixture.Renderer.DebugLayerErrorCount.ShouldBe(errorsBefore, fixture.Diagnostics);
    }

    [Fact]
    public void The_bridged_route_and_an_ordinary_srgb_target_encode_a_colour_the_same_way()
    {
        // The bridge copies from an _SRGB resource to a _UNORM one. It must be
        // a bit copy; a conversion washes the picture out with no error.
        Require();

        var linear = new Vector4(0.5f, 0.25f, 0.75f, 1f);
        RenderTarget reference = CreateReferenceTarget();
        try
        {
            fixture.Renderer.ClearForTest(reference, linear);
            fixture.WriteAndPublish(linear);

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
    public void A_double_encode_on_the_bridged_route_is_caught_rather_than_absorbed()
    {
        // The present target is written an already-encoded value, so its sRGB
        // view encodes it a second time.
        Require();

        var linear = new Vector4(0.5f, 0.25f, 0.75f, 1f);
        Vector3 alreadyEncoded = ColorSpace.LinearToSrgb(new Vector3(linear.X, linear.Y, linear.Z));

        RenderTarget reference = CreateReferenceTarget();
        try
        {
            fixture.Renderer.ClearForTest(reference, linear);
            fixture.WriteAndPublish(new Vector4(alreadyEncoded, 1f));

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
        SharedTargetD3D12Fixture.Width, SharedTargetD3D12Fixture.Height,
        TextureFormat.Rgba8, TextureColorSpace.Srgb, Depth: false));

    // The shared read goes through the bridge's texture, not the present
    // target, so it sees the copy. It also takes the consumer's turn, which
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

    // Needed between publishes. Without it the next write is skipped and the
    // mutex is left on key 1 for every later test.
    private static void TakeTurn(ConsumerDevice consumer)
    {
        consumer.Acquire(Renderer.SharedConsumerKey, 1000).ShouldBe(0);
        consumer.Release(Renderer.SharedProducerKey).ShouldBe(0);
    }
}

/// <summary>
/// A <see cref="D3D12Renderer"/> initialized against a composited surface, or a
/// recorded reason why not.
/// </summary>
// Tests skip on a machine with no D3D12 driver. Availability is probed with a
// throwaway device and nothing after that is caught, the 11On12 bridge
// included: a failure past the probe is a real defect.
public sealed unsafe class SharedTargetD3D12Fixture : IDisposable
{
    public const int Width = 64;

    public const int Height = 48;

    private readonly D3D12Renderer? _renderer;
    private readonly CompositedSurface _surface = new(Width, Height);
    private readonly RecordingLogger _log = new();

    public SharedTargetD3D12Fixture()
    {
        if (!DeviceIsAvailable(out string reason))
        {
            UnavailableReason = reason;
            Available = false;
            return;
        }

        var renderer = new D3D12Renderer(_log, new SpectraShadeCompiler()) { EnableDebugLayer = true };

        // A composited surface has no swap chain to ask, so this is the only size.
        renderer.SetFramebufferSize(new Vector2D<int>(Width, Height));
        renderer.Initialize(_surface);

        _renderer = renderer;
        Available = true;
        UnavailableReason = string.Empty;
    }

    private static bool DeviceIsAvailable(out string reason)
    {
        ID3D12Device* device = null;
        Guid guid = ID3D12Device.Guid;

        int hr = Silk.NET.Direct3D12.D3D12.GetApi().CreateDevice(
            (IUnknown*)null, D3DFeatureLevel.Level110, &guid, (void**)&device);

        if (device is not null) ((IUnknown*)device)->Release();

        reason = hr < 0 || device is null
            ? $"D3D12CreateDevice returned 0x{hr:X8}"
            : string.Empty;
        return reason.Length == 0;
    }

    public bool Available { get; }

    public string UnavailableReason { get; }

    public D3D12Renderer Renderer => _renderer
        ?? throw new InvalidOperationException("No D3D12 device; the test should have skipped.");

    /// <summary>
    /// Everything the renderer logged at warning or above, for assertion messages.
    /// </summary>
    public string Diagnostics => _log.Text;

    /// <summary>
    /// Clears the present target to <paramref name="color"/>, publishes it
    /// through the bridge, and ends the frame.
    /// </summary>
    // Present is what drains the debug layer; without it the error count is stale.
    public void WriteAndPublish(Vector4 color)
    {
        Renderer.WriteAndPublishForTest(color);
        Present();
    }

    /// <summary>Ends a frame: the fence wait, the ring maintenance and the debug-layer drain.</summary>
    public void Present() => Renderer.Present(_surface);

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

    private sealed class RecordingLogger : ILogger<Renderer>
    {
        private readonly List<string> _lines = [];

        internal string Text => _lines.Count == 0
            ? "(the renderer logged nothing above Information)"
            : string.Join(Environment.NewLine, _lines);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Warning and up only: the renderer logs a line per shader and target.
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                _lines.Add($"{logLevel}: {formatter(state, exception)}");
        }
    }
}
