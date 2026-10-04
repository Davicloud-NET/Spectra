namespace SpectraEngine.Editing.Viewport;

/// <summary>When the ground grid shows. <see cref="Auto"/> is the default.</summary>
public enum GridMode
{
    /// <summary>Shown while a move or resize gesture is live, faded out otherwise.</summary>
    Auto,

    /// <summary>Always drawn.</summary>
    On,

    /// <summary>Never drawn.</summary>
    Off,
}
