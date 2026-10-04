using System;

namespace SpectraEngine.Editing.Cameras;

/// <summary>Which way the editor camera is looking, and how it projects.</summary>
public enum EditorViewPreset
{
    /// <summary>The free perspective camera.</summary>
    Perspective,

    /// <summary>Looking down.</summary>
    Top,

    /// <summary>Looking up.</summary>
    Bottom,

    /// <summary>Looking along -Z, from the front.</summary>
    Front,

    /// <summary>Looking along +Z, from behind.</summary>
    Back,

    /// <summary>Looking along -X, from the right.</summary>
    Right,

    /// <summary>Looking along +X, from the left.</summary>
    Left,
}

/// <summary>Which world plane the grid is drawn on.</summary>
public enum GridPlane
{
    /// <summary>The floor, XZ, normal +Y.</summary>
    Ground,

    /// <summary>XY, normal +Z: what a front or back view looks at.</summary>
    Front,

    /// <summary>YZ, normal +X: what a left or right view looks at.</summary>
    Side,
}

/// <summary>The angles, grid plane and name behind each view.</summary>
// Y is up; a camera at yaw -pi/2 looks along -Z, the front view. A wrong angle
// here gives a mirrored view that still looks plausible.
public static class EditorViewPresets
{
    /// <summary>Whether this preset projects orthographic.</summary>
    public static bool IsOrthographic(EditorViewPreset preset) =>
        preset != EditorViewPreset.Perspective;

    /// <summary>The yaw a preset looks along.</summary>
    public static float YawOf(EditorViewPreset preset) => preset switch
    {
        // For top and bottom the yaw sets the screen axes: -pi/2 puts +X
        // right and -Z up.
        EditorViewPreset.Top => -MathF.PI * 0.5f,
        EditorViewPreset.Bottom => -MathF.PI * 0.5f,

        EditorViewPreset.Front => -MathF.PI * 0.5f,
        EditorViewPreset.Back => MathF.PI * 0.5f,
        EditorViewPreset.Right => MathF.PI,
        EditorViewPreset.Left => 0f,

        _ => 0f,
    };

    /// <summary>The pitch a preset looks along.</summary>
    public static float PitchOf(EditorViewPreset preset) => preset switch
    {
        EditorViewPreset.Top => -MathF.PI * 0.5f,
        EditorViewPreset.Bottom => MathF.PI * 0.5f,
        _ => 0f,
    };

    /// <summary>
    /// Which plane the grid is drawn on in this view: the one the view faces,
    /// since the floor grid seen edge-on is a line.
    /// </summary>
    public static GridPlane GridPlaneOf(EditorViewPreset preset) => preset switch
    {
        EditorViewPreset.Front or EditorViewPreset.Back => GridPlane.Front,
        EditorViewPreset.Right or EditorViewPreset.Left => GridPlane.Side,
        _ => GridPlane.Ground,
    };

    /// <summary>The display name. Literals, so no allocation per publish.</summary>
    public static string NameOf(EditorViewPreset preset) => preset switch
    {
        EditorViewPreset.Top => "Top",
        EditorViewPreset.Bottom => "Bottom",
        EditorViewPreset.Front => "Front",
        EditorViewPreset.Back => "Back",
        EditorViewPreset.Right => "Right",
        EditorViewPreset.Left => "Left",
        _ => "Perspective",
    };
}
