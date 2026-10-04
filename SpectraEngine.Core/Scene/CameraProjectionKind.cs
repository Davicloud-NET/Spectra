namespace SpectraEngine.Core.Scene;

/// <summary>How a camera turns world space into clip space.</summary>
public enum CameraProjectionKind
{
    /// <summary>Converging lines, as in a game camera.</summary>
    Perspective,

    /// <summary>Parallel lines, as in a plan view.</summary>
    Orthographic,
}
