using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Windowing;
using SpectraEngine.Core.Windowing;

namespace SpectraEngine.Graphics.Tests;

// Borrows the GL fixture's window: GLFW's window class is process-global and
// Silk's platform registration throws if re-run once a window exists.
/// <summary>The borderless-fullscreen transition against a real window.</summary>
[Collection(GlRendererCollection.Name)]
public sealed class BorderlessFullscreenWindowTests
{
    private readonly GlRendererFixture _fixture;

    public BorderlessFullscreenWindowTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void A_real_window_undecorates_fills_the_display_and_comes_back_unchanged()
    {
        IWindow window = _fixture.HostWindow;
        var target = new SilkWindowModeTarget(window);
        var latch = new WindowModeLatch(NullLogger.Instance);

        target.TryGetDisplayBounds(out WindowRect display).ShouldBeTrue();
        display.IsPositive.ShouldBeTrue();

        WindowRect windowed = target.Bounds;
        bool decorated = target.Decorated;

        try
        {
            latch.RequestWindowMode(WindowMode.BorderlessFullscreen);
            latch.ApplyPendingWindowMode(target).ShouldBe(WindowMode.BorderlessFullscreen);
            PumpEvents(window);

            target.Decorated.ShouldBeFalse();
            target.Bounds.ShouldBe(display);

            // Backends size their swap chain from the framebuffer.
            window.FramebufferSize.X.ShouldBe(display.Width);
            window.FramebufferSize.Y.ShouldBe(display.Height);

            latch.RequestWindowMode(WindowMode.Windowed);
            latch.ApplyPendingWindowMode(target).ShouldBe(WindowMode.Windowed);
            PumpEvents(window);

            target.Decorated.ShouldBeTrue();
            target.Bounds.ShouldBe(windowed);
        }
        finally
        {
            // The window is shared with the rest of the collection.
            target.Decorated = decorated;
            target.Bounds = windowed;
            PumpEvents(window);
        }
    }

    // GLFW applies attribute and geometry changes through its message queue.
    private static void PumpEvents(IWindow window)
    {
        for (int i = 0; i < 10; i++)
            window.DoEvents();
    }
}
