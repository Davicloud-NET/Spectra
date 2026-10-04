using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using System;
using System.IO;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Renders real frames into an offscreen target for a few frames, resizes it,
/// and fails if the graphics debug layer reported anything. This is the D3D
/// backends' render-target test: they have no headless device fixture.
/// </summary>
public sealed class OffscreenProbe
{
    private const int FramesPerStage = 3;

    // Each corner texel lands inside one quadrant of the 8x8 fixture.
    private const int OrientationTargetSize = 16;

    // Resize: reuse the previous stage's target at half size.
    private readonly record struct Stage(string What, TextureFormat Format, TextureColorSpace Space, bool Resize);

    private static readonly Stage[] Stages =
    [
        new("HDR linear", TextureFormat.Rgba16Float, TextureColorSpace.Linear, Resize: false),
        new("HDR linear, resized", TextureFormat.Rgba16Float, TextureColorSpace.Linear, Resize: true),
        new("8-bit sRGB", TextureFormat.Rgba8, TextureColorSpace.Srgb, Resize: false),
    ];

    private readonly ILogger _logger;
    private readonly int _width;
    private readonly int _height;

    private RenderTarget? _target;
    private int _stage = -1;
    private int _frames;
    private int _errorsAtStart;

    /// <summary>True until the probe has finished and reported.</summary>
    public bool Running { get; private set; } = true;

    /// <summary>Set once the probe has run to completion without throwing.</summary>
    public bool Passed { get; private set; }

    public OffscreenProbe(ILogger logger, int width = 640, int height = 360)
    {
        _logger = logger;
        _width = width;
        _height = height;
    }

    /// <summary>Call once per frame on the render thread, before <see cref="Renderer.Render"/>.</summary>
    public void Update(Renderer renderer)
    {
        if (!Running) return;

        try
        {
            if (_stage < 0)
            {
                // Baseline, so earlier errors are not blamed on the probe.
                _errorsAtStart = renderer.DebugLayerErrorCount;
                _logger.LogInformation(
                    "Offscreen probe: {Stages} stage(s), {Frames} frames each, starting at {Width}x{Height}",
                    Stages.Length, FramesPerStage, _width, _height);

                // Without the validation layer a D3D run only proves nothing threw.
                if (!renderer.DebugLayerActive && renderer.Backend != GraphicsBackend.OpenGL)
                {
                    _logger.LogWarning(
                        "Offscreen probe: the graphics validation layer is OFF, so this run cannot " +
                        "detect a missing barrier or a mismatched pipeline state. Re-run with " +
                        "--debug-layer=true for the full check.");
                }
                BeginStage(renderer, 0);
                return;
            }

            _frames++;
            if (_frames < FramesPerStage) return;

            if (_stage + 1 < Stages.Length)
            {
                BeginStage(renderer, _stage + 1);
                return;
            }

            bool orientationPassed = MeasureTextureOrientation(renderer);
            Finish(renderer, passed: orientationPassed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Offscreen probe: FAIL at stage {Stage}", Describe(_stage));
            Finish(renderer, passed: false);
        }
    }

    private void BeginStage(Renderer renderer, int index)
    {
        Stage stage = Stages[index];
        _stage = index;
        _frames = 0;

        if (stage.Resize && _target is not null)
        {
            _target.Resize(_width / 2, _height / 2);
            return;
        }

        if (_target is not null)
        {
            renderer.ProbeTarget = null;
            renderer.DestroyRenderTarget(_target);
        }

        // Not the window's size or aspect, so a viewport still taken from the
        // window shows up.
        _target = renderer.CreateRenderTarget(
            new RenderTargetDesc(_width, _height, stage.Format, stage.Space));
        renderer.ProbeTarget = _target;
    }

    private static string Describe(int index) =>
        index >= 0 && index < Stages.Length ? Stages[index].What : "setup";

    // Draws the asymmetric fixture and reads the four corners back to check
    // which way up an uploaded texture arrives. The readback's own orientation
    // is checked first, with plain geometry, so a readback bug is not reported
    // as a texture bug.
    private bool MeasureTextureOrientation(Renderer renderer)
    {
        // No synthetic fallback: a missing fixture fails the probe.
        string path = Path.Combine(
            ContentRoot.Path, TextureOrientationProbe.TexturePath.Replace('/', Path.DirectorySeparatorChar));
        DecodedImage image;
        try
        {
            image = ImageDecoder.DecodeFile(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Texture orientation: FAIL - the fixture {Path} could not be read, so nothing was measured", path);
            return false;
        }

        Texture? white = null;
        Texture? fixture = null;
        RenderTarget? target = null;
        try
        {
            target = renderer.CreateRenderTarget(new RenderTargetDesc(
                OrientationTargetSize, OrientationTargetSize, TextureFormat.Rgba8, TextureColorSpace.Linear));

            white = renderer.CreateTexture(
                [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
                TextureFilter.Nearest, TextureWrap.Clamp);
            renderer.DrawOrientationQuad(white, target, OrientationQuad.Coverage.TopHalf);

            (byte topR, _, _, _) = renderer.ReadTargetPixel(
                target, OrientationTargetSize / 2, OrientationTargetSize - 1);
            (byte bottomR, _, _, _) = renderer.ReadTargetPixel(target, OrientationTargetSize / 2, 0);
            if (topR < 120 || bottomR > 80)
            {
                _logger.LogError(
                    "Texture orientation: FAIL - the readback itself is wrong on {Backend}. A quad covering " +
                    "clip y 0..1 should light the top of the picture only, and the readback returned " +
                    "top={Top} bottom={Bottom}. No conclusion about textures can be drawn from this run.",
                    renderer.Backend, topR, bottomR);
                return false;
            }

            fixture = renderer.CreateTexture(
                image.Pixels, image.Width, image.Height, image.Format, TextureColorSpace.Linear,
                TextureFilter.Nearest, TextureWrap.Clamp);
            renderer.DrawOrientationQuad(fixture, target, OrientationQuad.Coverage.Full);

            int high = OrientationTargetSize - 1;
            var reading = new TextureOrientationProbe.Reading(
                ReadQuadrant(renderer, target, 0, high),
                ReadQuadrant(renderer, target, high, high),
                ReadQuadrant(renderer, target, 0, 0),
                ReadQuadrant(renderer, target, high, 0));

            if (reading.MatchesAuthoredImage)
            {
                _logger.LogInformation(
                    "Texture orientation on {Backend}: {Verdict} - {Reading}",
                    renderer.Backend, reading.Verdict, reading);
                return true;
            }

            _logger.LogError(
                "Texture orientation on {Backend}: {Verdict} - {Reading}. The engine's convention is that " +
                "an uploaded texture renders the way the image file was authored; this backend disagrees.",
                renderer.Backend, reading.Verdict, reading);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Texture orientation: FAIL - the measurement threw");
            return false;
        }
        finally
        {
            if (fixture is not null) renderer.DestroyTexture(fixture);
            if (white is not null) renderer.DestroyTexture(white);
            if (target is not null) renderer.DestroyRenderTarget(target);
        }
    }

    private static TextureOrientationProbe.Quadrant ReadQuadrant(
        Renderer renderer, RenderTarget target, int x, int y)
    {
        (byte r, byte g, byte b, _) = renderer.ReadTargetPixel(target, x, y);
        return TextureOrientationProbe.Classify(r, g, b);
    }

    private void Finish(Renderer renderer, bool passed)
    {
        renderer.ProbeTarget = null;
        if (_target is not null)
        {
            renderer.DestroyRenderTarget(_target);
            _target = null;
        }

        // On D3D the debug layer is the only report of a missing barrier or a
        // mismatched pipeline-state format.
        int newErrors = renderer.DebugLayerErrorCount - _errorsAtStart;
        if (passed && newErrors > 0)
        {
            passed = false;
            _logger.LogError(
                "Offscreen probe: FAIL - the graphics debug layer reported {Count} error(s) while " +
                "rendering into an offscreen target; see the messages above",
                newErrors);
        }

        Running = false;
        Passed = passed;

        if (passed)
        {
            _logger.LogInformation(
                "Offscreen probe: PASS - full frames rendered into {Stages} offscreen target(s) " +
                "({What}), the colour attachment kept its identity across an in-place resize, " +
                "the texture-orientation reading matched the authored image, and {Validation}",
                Stages.Length, string.Join(", ", Array.ConvertAll(Stages, x => x.What)),
                renderer.DebugLayerActive
                    ? "the debug layer stayed silent"
                    : "NO validation layer was running, so this is the weak form of the check");
        }
    }
}
