namespace SpectraEngine.Editing.Cameras;

/// <summary>Which navigation gesture owns the pointer. At most one at a time.</summary>
public enum EditorNavigationGesture
{
    /// <summary>No gesture is live.</summary>
    None,

    /// <summary>
    /// The camera turns in place and the movement keys fly it. Captures the cursor.
    /// </summary>
    FreeLook,

    /// <summary>The camera swings around its focus point at a fixed distance.</summary>
    Orbit,

    /// <summary>The focus slides in the camera's view plane.</summary>
    Pan,
}
