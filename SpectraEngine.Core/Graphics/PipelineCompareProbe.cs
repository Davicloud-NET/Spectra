using Microsoft.Extensions.Logging;
using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Renders the scene through the deferred pipeline, then the forward one, into
/// one offscreen target, and reports whether the two pictures agree. The paths
/// share no shader, so nothing else notices when one of them drifts.
/// </summary>
// PipelineParityGlTests covers OpenGL. This is the same check for the D3D
// backends, which have no headless fixture.
// The pictures are frames apart, so the scene has to hold still. A second
// deferred picture is taken at the end to prove that it did.
// Render thread only, before Renderer.Render.
public sealed class PipelineCompareProbe
{
    private const string Deferred = "Deferred";
    private const string Forward = "Forward";

    // Textures and world chunks arrive over the first frames.
    private const int WarmupFrames = 90;

    // A pipeline's first frames create its targets and programs.
    private const int SettleFrames = 4;

    private enum Stage
    {
        Warmup,
        Deferred,
        Forward,
        DeferredAgain,
    }

    private readonly ILogger _logger;

    private RenderTarget? _target;
    private int _width;
    private int _height;
    private Stage _stage = Stage.Warmup;
    private int _frames;
    private int _errorsAtStart;
    private string? _restorePipeline;
    private byte[]? _deferred;
    private byte[]? _forward;

    /// <summary>True until the probe has finished and reported.</summary>
    public bool Running { get; private set; } = true;

    /// <summary>Set once the probe has run to completion and the two pictures agreed.</summary>
    public bool Passed { get; private set; }

    public PipelineCompareProbe(ILogger logger) => _logger = logger;

    /// <summary>Call once per frame on the render thread, before <see cref="Renderer.Render"/>.</summary>
    public void Update(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (!Running) return;

        try
        {
            if (_frames == 0 && _stage == Stage.Warmup)
            {
                _errorsAtStart = renderer.DebugLayerErrorCount;
                _restorePipeline = renderer.CurrentPipelineName;
                _logger.LogInformation(
                    "Pipeline compare: warming up {Frames} frames on {Backend}, then one picture each from " +
                    "{First} and {Second}.",
                    WarmupFrames, renderer.Backend, Deferred, Forward);
            }

            _frames++;

            switch (_stage)
            {
                case Stage.Warmup:
                    if (_frames < WarmupFrames) return;

                    // The window's size. The G-buffer is the window's size
                    // whatever the target, so at any other the deferred
                    // picture is a resampled one and the two cannot match.
                    renderer.GetFramebufferSize(out _width, out _height);
                    if (_width <= 0 || _height <= 0)
                        throw new InvalidOperationException("The window has no size to render at.");

                    // sRGB 8-bit: what a window would be given.
                    _target = renderer.CreateRenderTarget(
                        new RenderTargetDesc(_width, _height, TextureFormat.Rgba8, TextureColorSpace.Srgb));
                    renderer.ProbeTarget = _target;
                    Enter(renderer, Stage.Deferred, Deferred);
                    return;

                case Stage.Deferred:
                    if (_frames < SettleFrames) return;
                    _deferred = Read(renderer);
                    Enter(renderer, Stage.Forward, Forward);
                    return;

                case Stage.Forward:
                    if (_frames < SettleFrames) return;
                    _forward = Read(renderer);
                    Enter(renderer, Stage.DeferredAgain, Deferred);
                    return;

                case Stage.DeferredAgain:
                    if (_frames < SettleFrames) return;
                    Report(renderer, Read(renderer));
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline compare: FAIL - the measurement threw");
            Finish(renderer, passed: false);
        }
    }

    private void Enter(Renderer renderer, Stage stage, string pipeline)
    {
        if (!renderer.TrySelectPipeline(pipeline))
            throw new InvalidOperationException($"{renderer.Backend} has no pipeline named '{pipeline}'.");

        _stage = stage;
        _frames = 0;
    }

    // The probe target holds the last frame's picture.
    private byte[] Read(Renderer renderer)
    {
        byte[] picture = new byte[PixelReadback.ByteCount(_width, _height)];
        renderer.ReadTargetPixels(_target!, picture);
        return picture;
    }

    private void Report(Renderer renderer, byte[] deferredAgain)
    {
        byte[] deferred = _deferred!;
        byte[] forward = _forward!;

        // Two blank pictures agree perfectly.
        if (!ViewportCompare.HasVariation(deferred))
        {
            _logger.LogError(
                "Pipeline compare: FAIL - the deferred picture is a single flat colour, so an agreement " +
                "would prove nothing. The frame drew no scene; check the loaded map.");
            Finish(renderer, passed: false);
            return;
        }

        PipelineCompare.Reading still = PipelineCompare.Compare(deferred, deferredAgain);
        if (!still.Passes)
        {
            _logger.LogError(
                "Pipeline compare: FAIL - the scene moved while it was measured ({Reading} between two " +
                "deferred pictures), so nothing can be said about the pipelines. Something in it is " +
                "animating or still loading.",
                still);
            Finish(renderer, passed: false);
            return;
        }

        PipelineCompare.Reading reading = PipelineCompare.Compare(deferred, forward);
        if (reading.Passes)
        {
            _logger.LogInformation("Pipeline compare on {Backend}: PASS - {Reading}", renderer.Backend, reading);
        }
        else
        {
            int x = reading.WorstPixel % _width;
            int y = reading.WorstPixel / _width;
            int i = reading.WorstPixel * PixelReadback.BytesPerPixel;
            _logger.LogError(
                "Pipeline compare on {Backend}: FAIL - {Reading}. At ({X}, {Y}) deferred is " +
                "({DeferredR}, {DeferredG}, {DeferredB}) and forward is ({ForwardR}, {ForwardG}, {ForwardB}). " +
                "The two shaders share their lighting text, so look at what each is given: the uniforms " +
                "DrawLit uploads against DrawDeferredLightPass, and what the G-buffer stores.",
                renderer.Backend, reading, x, y,
                deferred[i], deferred[i + 1], deferred[i + 2],
                forward[i], forward[i + 1], forward[i + 2]);
        }

        Finish(renderer, reading.Passes);
    }

    private void Finish(Renderer renderer, bool passed)
    {
        renderer.ProbeTarget = null;
        if (_target is not null)
        {
            renderer.DestroyRenderTarget(_target);
            _target = null;
        }

        if (_restorePipeline is { } restore)
            renderer.TrySelectPipeline(restore);

        // A matching picture still fails if the debug layer reported errors.
        int newErrors = renderer.DebugLayerErrorCount - _errorsAtStart;
        if (passed && newErrors > 0)
        {
            passed = false;
            _logger.LogError(
                "Pipeline compare: FAIL - the graphics debug layer reported {Count} error(s) while the two " +
                "pipelines ran; see the messages above",
                newErrors);
        }

        Running = false;
        Passed = passed;
    }
}
