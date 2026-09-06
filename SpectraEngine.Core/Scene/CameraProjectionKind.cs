namespace SpectraEngine.Core.Scene;

/// <summary>How a camera turns world space into clip space.</summary>
/// <remarks>
/// <b>Two values and no third, deliberately.</b> Every projection an editor
/// needs is one of these; an oblique or a two-point projection is a drawing
/// convention rather than a camera, and adding one here would put a third arm in
/// every screen-space size calculation in the engine.
/// </remarks>
public enum CameraProjectionKind
{
    /// <summary>Converging, which is what a game camera is.</summary>
    Perspective,

    /// <summary>Parallel, which is what a plan view is.</summary>
    Orthographic,
}
