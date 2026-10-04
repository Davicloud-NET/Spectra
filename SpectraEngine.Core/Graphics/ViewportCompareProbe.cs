using Microsoft.Extensions.Logging;
using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Renders one frame into the shared present target and an ordinary sRGB
/// target, reads both back, and reports the largest per-channel difference.
/// Catches a double sRGB encode on the shared route, which raises no error.
/// </summary>
// Both targets are written in the same frame: two frames would differ by the
// animation. Each update also takes the consumer's turn on the keyed mutex,
// or the producer skips every shared write after the first.
// Render thread only, before Renderer.Render.
public sealed class ViewportCompareProbe
{
    // The shared target does not exist until the first frame has created it.
    private const int WarmupFrames = 4;

    private readonly ILogger _logger;

    private RenderTarget? _reference;
    private int _frames;
    private bool _armed;
    private int _errorsAtStart;

    /// <summary>True until the probe has finished and reported.</summary>
    public bool Running { get; private set; } = true;

    /// <summary>Set once the probe has run to completion and the two pictures agreed.</summary>
    public bool Passed { get; private set; }

    /// <summary>What the comparison measured, once it has run.</summary>
    public ViewportCompare.Reading Reading { get; private set; }

    public ViewportCompareProbe(ILogger logger) => _logger = logger;

    /// <summary>Call once per frame on the render thread, before <see cref="Renderer.Render"/>.</summary>
    public void Update(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (!Running) return;

        try
        {
            if (_frames == 0) Begin(renderer);
            _frames++;

            // No consumer turn before the read: the read takes it itself, and a
            // second turn hands key 0 back so the read's acquire of key 1 times out.
            if (_armed)
            {
                Measure(renderer);
                return;
            }

            // Nothing else in this process hands key 0 back to the producer.
            renderer.TakeSharedConsumerTurn();

            if (_frames >= WarmupFrames) Arm(renderer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Viewport compare: FAIL - the measurement threw");
            Finish(renderer, passed: false);
        }
    }

    private void Begin(Renderer renderer)
    {
        _errorsAtStart = renderer.DebugLayerErrorCount;
        _logger.LogInformation(
            "Viewport compare: warming up {Frames} frame(s) on {Backend}, then rendering one frame into the " +
            "shared present target and an ordinary sRGB target at once. Passes at a max per-channel delta of " +
            "{Threshold} or less.",
            WarmupFrames, renderer.Backend, ViewportCompare.Threshold);

        if (!renderer.DebugLayerActive && renderer.Backend != GraphicsBackend.OpenGL)
        {
            _logger.LogWarning(
                "Viewport compare: the graphics validation layer is OFF, so this run measures the picture " +
                "only. Re-run with --debug-layer=true to gate the shared route's barriers as well.");
        }
    }

    private void Arm(Renderer renderer)
    {
        if (!renderer.TryGetSharedHandle(out Renderer.SharedTargetHandle handle))
        {
            _logger.LogError(
                "Viewport compare: FAIL - {Backend} has no shared present target, so there is nothing to " +
                "compare against. This probe needs a composited surface.",
                renderer.Backend);
            Finish(renderer, passed: false);
            return;
        }

        // With HDR off the pipeline draws straight into the presented target,
        // so there is no intermediate to resolve a second time.
        if (!renderer.HdrEnabled)
        {
            _logger.LogError(
                "Viewport compare: FAIL - HDR is off, so the frame has no intermediate to resolve twice from " +
                "and no reference picture can be produced.");
            Finish(renderer, passed: false);
            return;
        }

        // Rgba8 sRGB: the same format as the window's back buffer on both backends.
        _reference = renderer.CreateRenderTarget(new RenderTargetDesc(
            handle.Width, handle.Height, TextureFormat.Rgba8, TextureColorSpace.Srgb, Depth: false));
        renderer.CompareTarget = _reference;
        _armed = true;

        _logger.LogInformation(
            "Viewport compare: armed at {Width}x{Height} (shared generation {Generation}, handle 0x{Handle:X}).",
            handle.Width, handle.Height, handle.Generation, handle.NtHandle);
    }

    private void Measure(Renderer renderer)
    {
        RenderTarget reference = _reference
            ?? throw new InvalidOperationException("The compare probe measured before it armed.");

        // Disarm first: the read below can throw.
        renderer.CompareTarget = null;

        byte[] windowPicture = new byte[PixelReadback.ByteCount(reference.Width, reference.Height)];
        renderer.ReadTargetPixels(reference, windowPicture);

        // Two blank pictures agree perfectly, so a frame that drew nothing must fail.
        if (!ViewportCompare.HasVariation(windowPicture))
        {
            _logger.LogError(
                "Viewport compare: FAIL - the reference picture is a single flat colour, so an agreement " +
                "would prove nothing. The frame drew no scene; check the pipeline and the loaded map.");
            Finish(renderer, passed: false);
            return;
        }

        byte[] sharedPicture = new byte[windowPicture.Length];
        if (!renderer.TryReadSharedPixels(sharedPicture))
        {
            _logger.LogError(
                "Viewport compare: FAIL - the shared target's key never came back, so nothing was read " +
                "through the handle and no comparison was made.");
            Finish(renderer, passed: false);
            return;
        }

        Reading = ViewportCompare.Compare(windowPicture, sharedPicture);

        if (Reading.Passes)
        {
            _logger.LogInformation(
                "Viewport compare on {Backend}: {Verdict} - {Reading}",
                renderer.Backend, Reading.Verdict, Reading);
        }
        else
        {
            _logger.LogError(
                "Viewport compare on {Backend}: {Verdict} - {Reading}. A delta of this size on a picture " +
                "both routes drew in one frame is a transfer function applied twice: check that the shared " +
                "resource is UNORM with only its render-target view sRGB.",
                renderer.Backend, Reading.Verdict, Reading);
        }

        Finish(renderer, Reading.Passes);
    }

    private void Finish(Renderer renderer, bool passed)
    {
        renderer.CompareTarget = null;
        if (_reference is not null)
        {
            renderer.DestroyRenderTarget(_reference);
            _reference = null;
        }

        // A matching picture still fails if the debug layer reported errors.
        int newErrors = renderer.DebugLayerErrorCount - _errorsAtStart;
        if (passed && newErrors > 0)
        {
            passed = false;
            _logger.LogError(
                "Viewport compare: FAIL - the graphics debug layer reported {Count} error(s) while rendering " +
                "into a shared target; see the messages above",
                newErrors);
        }

        Running = false;
        Passed = passed;
    }
}
