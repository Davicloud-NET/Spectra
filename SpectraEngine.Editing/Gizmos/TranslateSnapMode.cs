namespace SpectraEngine.Editing.Gizmos;

/// <summary>What a snapped translate drag quantises.</summary>
public enum TranslateSnapMode
{
    /// <summary>
    /// Quantise the drag's displacement: a part at x = 0.3 dragged one unit
    /// lands at 1.3. The default, as in Roblox Studio and Blender.
    /// </summary>
    Delta,

    /// <summary>
    /// Quantise the reference node's destination onto the world grid; the other
    /// nodes keep their offsets from it. World orientation only: local drags
    /// always snap the displacement.
    /// </summary>
    AbsoluteGrid,
}
