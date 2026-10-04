namespace SpectraEngine.Core.Bsp;

/// <summary>
/// Whether a brush adds solid to the compiled world or removes it. The world
/// is the union of the additive brushes minus the union of the subtractive
/// ones, so every subtractive brush beats every additive one.
/// </summary>
// Unordered on purpose. The only order over brushes is scene traversal order,
// and an ordered subtraction would let a reparent turn a doorway into a wall.
// An additive brush inside a subtractive one is gone. Fill a hole with a Part brush.
public enum BrushOperation : byte
{
    /// <summary>The brush contributes its solid. The default.</summary>
    Additive = 0,

    /// <summary>
    /// The brush removes its solid from every additive brush it overlaps. The
    /// cavity walls wear this brush's face materials.
    /// </summary>
    Subtractive = 1,
}
