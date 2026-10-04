using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// The editor's ground: a grid at the live snap increment, with the world axes
/// running through the origin.
/// </summary>
// Emits whole lines. The distance fade is per pixel in the world-line shaders,
// driven by the fade values written to DebugDraw here.
public sealed class GroundGrid
{
    /// <summary>
    /// The smallest a minor cell may project to, in screen pixels. Below it the
    /// cell size doubles, so the drawn cell can be a multiple of the snap.
    /// </summary>
    public const float MinimumCellPixels = 12f;

    /// <summary>
    /// How large the finer cell must project before the grid refines back to it.
    /// </summary>
    // Above MinimumCellPixels: the gap is hysteresis, or a camera at the
    // threshold flips the lattice every frame.
    public const float RefineCellPixels = 16f;

    /// <summary>How many minor cells make one major cell.</summary>
    public const int MajorEvery = 5;

    /// <summary>
    /// The most lines the grid may emit in one frame, across both directions.
    /// The two axis lines are not counted.
    /// </summary>
    public int MaxLines { get; set; } = 512;

    /// <summary>Whether the grid draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whole-grid alpha, 0..1. Multiplies the per-pixel fade.</summary>
    public float Opacity { get; set; } = 1f;

    private const float MinimumOpacity = 0.005f;

    /// <summary>
    /// How far the grid extends from the camera's ground point, in world units.
    /// Grows with the camera's height, up to double.
    /// </summary>
    public float Radius { get; set; } = 48f;

    // Fractions of the continuous radius, not the step-quantised reach, which
    // jumps a cell at a time.
    private const float FadeStartFraction = 0.05f;
    private const float FadeEndFraction = 0.62f;

    // Linear light: these lines blend into the scene before the tone curve.
    // Dark, so they read against both a bright sky and a pale floor.
    private static readonly Vector3 MinorColor = new(0.030f, 0.029f, 0.028f);
    private static readonly Vector3 MajorColor = new(0.011f, 0.011f, 0.012f);

    /// <summary>Lines the last <see cref="Draw"/> emitted, axes included.</summary>
    public int DrawnLastDraw { get; private set; }

    /// <summary>
    /// Lines the last <see cref="Draw"/> could not emit because
    /// <see cref="MaxLines"/> was reached.
    /// </summary>
    public int SkippedLastDraw { get; private set; }

    /// <summary>
    /// The cell size the last <see cref="Draw"/> used. Not always the snap
    /// increment; see <see cref="MinimumCellPixels"/>.
    /// </summary>
    public float CellSizeLastDraw { get; private set; }

    // Last frame's answer, for the hysteresis.
    private float _lastCell;
    private float _lastIncrement;

    /// <summary>
    /// Emits the grid into <paramref name="output"/>, sized to
    /// <paramref name="camera"/> and spaced by <paramref name="increment"/>.
    /// </summary>
    /// <param name="output">The depth-tested world-line buffer, never the overlay.</param>
    /// <param name="increment">The live translate snap, in world units.</param>
    public void Draw(DebugDraw output, Camera camera, float increment, float viewportHeight) =>
        Draw(output, camera, increment, viewportHeight, GridPlane.Ground);

    /// <summary>Draws the grid on one of the three world planes.</summary>
    public void Draw(
        DebugDraw output, Camera camera, float increment, float viewportHeight, GridPlane plane)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(camera);

        DrawnLastDraw = 0;
        SkippedLastDraw = 0;
        CellSizeLastDraw = 0f;

        if (!Enabled || Opacity <= MinimumOpacity || increment <= 0f || viewportHeight <= 0f)
            return;

        // Measured from the camera, not the origin, so the grid follows the user.
        Vector3 eye = camera.Position;

        // Two in-plane axes and the one across. A table, because a transposed
        // axis draws a normal-looking grid on the wrong plane.
        (int uAxis, int vAxis, int nAxis) = plane switch
        {
            GridPlane.Front => (0, 1, 2),
            GridPlane.Side => (2, 1, 0),
            _ => (0, 2, 1),
        };

        // Orthographic: the eye sits at the focus, so its distance from the
        // plane says nothing about zoom. Use the view height instead.
        float height = camera.ProjectionKind == CameraProjectionKind.Orthographic
            ? MathF.Max(camera.OrthographicHeight, 1f)
            : MathF.Max(MathF.Abs(Component(eye, nAxis)), 1f);

        float radius = MathF.Min(Radius * MathF.Max(1f, height / 10f), Radius * 2f);

        float cell = CoarsenedCell(increment, height, camera, viewportHeight);
        CellSizeLastDraw = cell;

        // Snap the centre to the major spacing, or the major lines slide as the
        // camera moves. The re-centre jump is hidden past the fade's end.
        float major = cell * MajorEvery;
        float centerU = MathF.Floor(Component(eye, uAxis) / major) * major;
        float centerV = MathF.Floor(Component(eye, vAxis) / major) * major;

        int steps = (int)MathF.Ceiling(radius / cell);

        int wanted = 2 * ((2 * steps) + 1);
        if (wanted > MaxLines)
        {
            // Shrink the extent rather than stop half way across the screen.
            steps = Math.Max(1, (MaxLines / 4) - 1);
            SkippedLastDraw = wanted - (2 * ((2 * steps) + 1));
        }

        float reach = steps * cell;

        // Clamped to the reach when the cap shrank the patch, or lines would
        // end mid-fade in a square edge.
        float fadeRadius = MathF.Min(radius, reach);
        output.FadeCenter = OnPlane(uAxis, vAxis, Component(eye, uAxis), Component(eye, vAxis));
        output.FadeStart = fadeRadius * FadeStartFraction;
        output.FadeEnd = fadeRadius * FadeEndFraction;
        output.Opacity = Opacity;

        for (int i = -steps; i <= steps; i++)
        {
            float u = centerU + (i * cell);
            float v = centerV + (i * cell);

            // Major by world position, not by index from the camera, or the
            // major lines move every time the patch re-centres.
            bool majorU = IsMultiple(u, major);
            bool majorV = IsMultiple(v, major);

            output.Line(
                OnPlane(uAxis, vAxis, u, centerV - reach),
                OnPlane(uAxis, vAxis, u, centerV + reach),
                majorU ? MajorColor : MinorColor);

            output.Line(
                OnPlane(uAxis, vAxis, centerU - reach, v),
                OnPlane(uAxis, vAxis, centerU + reach, v),
                majorV ? MajorColor : MinorColor);

            DrawnLastDraw += 2;
        }

        DrawAxes(output, uAxis, vAxis, centerU, centerV, reach);
        DrawnLastDraw += 2;
    }

    private static float Component(Vector3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z,
    };

    // Third coordinate is zero: the grid is a plane through the origin.
    private static Vector3 OnPlane(int uAxis, int vAxis, float u, float v)
    {
        Vector3 point = Vector3.Zero;

        if (uAxis == 0) point.X = u; else if (uAxis == 1) point.Y = u; else point.Z = u;
        if (vAxis == 0) point.X = v; else if (vAxis == 1) point.Y = v; else point.Z = v;

        return point;
    }

    // Doubles the cell until it projects to MinimumCellPixels, halves it back
    // only past RefineCellPixels. Measured at the camera's height, where the
    // user is working; the far cells have faded out.
    private float CoarsenedCell(float increment, float height, Camera camera, float viewportHeight)
    {
        float worldPerPixel = Gizmos.GizmoMath.WorldPerPixel(camera, viewportHeight, height);
        if (worldPerPixel <= 0f || !float.IsFinite(worldPerPixel))
            return increment;

        // Start from last frame's level; a changed increment resets it.
        float cell = _lastIncrement == increment && _lastCell >= increment
            ? _lastCell
            : increment;

        // Bounded so a degenerate camera cannot spin here.
        for (int i = 0; i < 32 && cell / worldPerPixel < MinimumCellPixels; i++)
            cell *= 2f;

        // Halving exact doublings lands back on the increment bit for bit.
        for (int i = 0; i < 32 && cell > increment && (cell * 0.5f) / worldPerPixel >= RefineCellPixels; i++)
            cell *= 0.5f;

        _lastIncrement = increment;
        _lastCell = cell;
        return cell;
    }

    // Always emitted: an axis far from the camera fades to zero alpha, where a
    // distance gate would pop it.
    private static void DrawAxes(
        DebugDraw output, int uAxis, int vAxis, float centerU, float centerV, float reach)
    {
        // Hue follows the world axis, not the row: x is red on every plane.
        output.Line(
            OnPlane(uAxis, vAxis, centerU - reach, 0f),
            OnPlane(uAxis, vAxis, centerU + reach, 0f),
            AxisColor(uAxis));

        output.Line(
            OnPlane(uAxis, vAxis, 0f, centerV - reach),
            OnPlane(uAxis, vAxis, 0f, centerV + reach),
            AxisColor(vAxis));
    }

    private static Vector3 AxisColor(int axis) => axis switch
    {
        0 => new Vector3(0.30f, 0.020f, 0.020f),
        1 => new Vector3(0.020f, 0.26f, 0.030f),
        _ => new Vector3(0.020f, 0.035f, 0.28f),
    };

    // Tolerant: exact equality on these floats makes the major lines flicker.
    private static bool IsMultiple(float value, float spacing)
    {
        float ratio = value / spacing;
        return MathF.Abs(ratio - MathF.Round(ratio)) < 1e-3f;
    }
}
