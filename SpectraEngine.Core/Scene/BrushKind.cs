namespace SpectraEngine.Core.Scene;

/// <summary>
/// Whether a node's <see cref="SceneNode.Brush"/> is admitted to the fused
/// static world, or stands alone as a movable object.
/// Per node and never inherited, so a reparent cannot change what is carved.
/// </summary>
public enum BrushKind : byte
{
    /// <summary>
    /// World geometry: carved against its neighbours and fused into the
    /// compiled chunk meshes.
    /// </summary>
    World = 0,

    /// <summary>
    /// A standalone object with its own mesh, never carved. Moving one costs no
    /// static-world recompile, so anything simulated must be a part.
    /// </summary>
    Part = 1,
}
