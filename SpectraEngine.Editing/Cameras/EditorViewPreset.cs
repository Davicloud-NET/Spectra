using System;

namespace SpectraEngine.Editing.Cameras;

/// <summary>Which way the editor camera is looking, and how it projects.</summary>
public enum EditorViewPreset
{
    /// <summary>The free camera: converging, and pointed wherever it was left.</summary>
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

/// <summary>
/// The axis facts behind each view, in one table.
/// </summary>
/// <remarks>
/// <para>
/// <b>A table rather than six methods, because the mistakes here are
/// transpositions.</b> "Front is a yaw of -pi/2" is not something anybody can
/// check by reading, and getting it wrong renders a picture that is plausible
/// and mirrored: a level drawn back to front is not obviously wrong until
/// somebody builds half a room against it. The tests pin the SCREEN axes rather
/// than the angles, which is the form a person can verify.
/// </para>
/// <para>
/// Y is up and the default forward is -Z, so a camera at yaw -pi/2 looks along
/// -Z, which is the front view.
/// </para>
/// </remarks>
public static class EditorViewPresets
{
    /// <summary>Whether this preset projects orthographic.</summary>
    public static bool IsOrthographic(EditorViewPreset preset) =>
        preset != EditorViewPreset.Perspective;

    /// <summary>The yaw a preset looks along.</summary>
    public static float YawOf(EditorViewPreset preset) => preset switch
    {
        // Top and bottom look straight down and up; the yaw decides which way
        // the world's axes lie on the screen rather than which way the camera
        // faces, and -pi/2 puts +X to the right and -Z up, which is how every
        // editor in this category draws a plan.
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

    /// <summary>Which plane the grid belongs on in this view.</summary>
    /// <remarks>
    /// <b>An edge-on grid is a line, which is worse than none.</b> A front view
    /// looking at the floor grid sees one row of pixels, so the grid moves to
    /// the plane the view is actually looking at; the question it answers, "what
    /// will this snap to", is the same in every view.
    /// </remarks>
    public static GridPlane GridPlaneOf(EditorViewPreset preset) => preset switch
    {
        EditorViewPreset.Front or EditorViewPreset.Back => GridPlane.Front,
        EditorViewPreset.Right or EditorViewPreset.Left => GridPlane.Side,
        _ => GridPlane.Ground,
    };

    /// <summary>
    /// The word a status bar shows. Interned, because it crosses the frame
    /// snapshot every publish.
    /// </summary>
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
