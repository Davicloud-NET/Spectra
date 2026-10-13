using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Core.Inspection;

/// <summary>One attribute of the selection, merged over the nodes that have it.</summary>
/// <param name="Name">The attribute's name.</param>
/// <param name="Kind">
/// The kind of its value, or <see cref="MemberValueKind.None"/> when the
/// nodes that have it hold values of different kinds.
/// </param>
/// <param name="Value">The value on the first node that has it.</param>
/// <param name="Count">How many of the selected nodes have it.</param>
/// <param name="MixedParts">
/// Which parts of the value differ between the nodes, one bit for each part
/// <see cref="AttributeParts"/> counts. A value that is edited whole has bit 0.
/// </param>
/// <param name="IsTakenByEntity">
/// Whether a node that has it carries an entity with a keyvalue of the name.
/// Scripts then read the keyvalue and never this.
/// </param>
public readonly record struct AttributePanelRow(
    string Name, MemberValueKind Kind, MemberValue Value, int Count, int MixedParts, bool IsTakenByEntity)
{
    /// <summary>Whether the nodes that have the attribute disagree about any of it.</summary>
    public bool IsMixed => MixedParts != 0;

    /// <summary>
    /// How many of the selected nodes lack the attribute and would not get it
    /// from a new value. The editor leaves those as they are.
    /// </summary>
    public int LeftOutCount { get; init; }

    /// <summary>What stands in the way on the nodes that are left out.</summary>
    public AttributeRoom LeftOut { get; init; }
}
