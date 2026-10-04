namespace SpectraEngine.Editing.Hosting;

/// <summary>What <see cref="SceneEditorHost.Insert"/> creates.</summary>
public enum InsertKind
{
    /// <summary>A 2x2x2 box brush fused into the static world.</summary>
    WorldBrush,

    /// <summary>The same box as a part: outside the carve, free to move cheaply.</summary>
    PartBrush,

    /// <summary>
    /// The same box subtracting instead of adding. Always world-kind: a
    /// subtractive part carves nothing and draws nothing.
    /// </summary>
    SubtractiveBrush,

    /// <summary>A point light, lifted clear of the surface it was aimed at.</summary>
    PointLight,

    /// <summary>
    /// A rect light lying flat on the surface under the cursor, facing out of
    /// it, and parented to whatever owns that surface so it moves with it.
    /// </summary>
    SurfaceLight,

    /// <summary>An empty node, for organising what exists.</summary>
    Group,
}
