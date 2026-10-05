using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.D3D11;
using SpectraEngine.Core.Graphics.D3D12;
using SpectraShade.Compiler;
using System;
using System.Numerics;
using Xunit;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// A lost graphics device, faked against a real driver: what the next present
/// does, and what a renderer that has stopped drawing still owes a consumer.
/// </summary>
[Collection(D3DDeviceCollection.Name)]
public sealed class DeviceLossTests(SharedTargetD3D11Fixture d3d11, SharedTargetD3D12Fixture d3d12)
{
    private const int Width = 64;
    private const int Height = 48;

    // WAIT_TIMEOUT. AcquireSync returns it as a positive HRESULT.
    private const int WaitTimeout = 0x00000102;

    private void RequireD3D11() => Assert.SkipWhen(
        !d3d11.Available, $"no usable D3D11 device in this process: {d3d11.UnavailableReason}");

    private void RequireD3D12() => Assert.SkipWhen(
        !d3d12.Available, $"no usable D3D12 device in this process: {d3d12.UnavailableReason}");

    // A renderer of its own: a loss leaves it fit for nothing but Shutdown.
    private static void WithRenderer(Renderer renderer, Action<Renderer, IRenderSurface> test)
    {
        var surface = new CompositedSurface(Width, Height);
        renderer.SetFramebufferSize(surface.PixelSize);
        renderer.Initialize(surface);

        try
        {
            test(renderer, surface);
        }
        finally
        {
            renderer.Shutdown();
        }
    }

    private static void AFakedLossEndsTheNextPresent(Renderer renderer, IRenderSurface surface)
    {
        renderer.CanLoseDevice.ShouldBeTrue();
        renderer.Present(surface);

        renderer.SimulateDeviceLoss();

        GraphicsDeviceLostException lost = Should.Throw<GraphicsDeviceLostException>(
            () => renderer.Present(surface));
        lost.Message.ShouldContain("DXGI_ERROR_DEVICE_REMOVED");

        // The renderer now treats the device as gone, as after a real loss.
        Should.NotThrow(() => renderer.Present(surface));
    }

    [Fact]
    public void A_faked_loss_ends_the_next_d3d11_present_the_way_a_real_one_does()
    {
        RequireD3D11();

        WithRenderer(
            new D3D11Renderer(NullLogger<Renderer>.Instance, new SpectraShadeCompiler()),
            AFakedLossEndsTheNextPresent);
    }

    [Fact]
    public void A_faked_loss_ends_the_next_d3d12_present_the_way_a_real_one_does()
    {
        RequireD3D12();

        WithRenderer(
            new D3D12Renderer(NullLogger<Renderer>.Instance, new SpectraShadeCompiler()),
            (renderer, surface) =>
            {
                // Work on the queue, so the loss lands on a device that is busy.
                ((D3D12Renderer)renderer).WriteAndPublishForTest(new Vector4(0.2f, 0.4f, 0.6f, 1f));
                AFakedLossEndsTheNextPresent(renderer, surface);
            });
    }

    // The compositor queues its turns and waits for each with no deadline. An
    // engine that died without answering them froze the whole editor window.
    private static void AnswersEveryQueuedTurn(Renderer renderer)
    {
        renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle).ShouldBeTrue();
        using var consumer = new ConsumerDevice(handle.NtHandle);

        // Whatever an earlier test left on offer.
        if (consumer.Acquire(Renderer.SharedConsumerKey, 0) == 0)
            consumer.Release(Renderer.SharedProducerKey).ShouldBe(0);

        consumer.Acquire(Renderer.SharedConsumerKey, 0).ShouldBe(
            WaitTimeout, "nothing was drawn, so the consumer has no turn yet");

        for (int turn = 0; turn < 3; turn++)
        {
            renderer.OfferSharedTurn();

            consumer.Acquire(Renderer.SharedConsumerKey, 1000).ShouldBe(0);
            consumer.Release(Renderer.SharedProducerKey).ShouldBe(0);
        }

        // Offering with no taker must not block, or leave the key stuck.
        renderer.OfferSharedTurn();
        renderer.OfferSharedTurn();
        consumer.Acquire(Renderer.SharedConsumerKey, 1000).ShouldBe(0);
        consumer.Release(Renderer.SharedProducerKey).ShouldBe(0);

        // And the producer can still draw afterwards.
        renderer.BeginSharedWrite(1000).ShouldBeTrue();
        renderer.EndSharedWrite();
        consumer.Acquire(Renderer.SharedConsumerKey, 1000).ShouldBe(0);
        consumer.Release(Renderer.SharedProducerKey).ShouldBe(0);
    }

    [Fact]
    public void A_d3d11_renderer_that_has_stopped_drawing_still_answers_every_queued_turn()
    {
        RequireD3D11();
        AnswersEveryQueuedTurn(d3d11.Renderer);
    }

    [Fact]
    public void A_d3d12_renderer_that_has_stopped_drawing_still_answers_every_queued_turn()
    {
        RequireD3D12();
        AnswersEveryQueuedTurn(d3d12.Renderer);
    }

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
