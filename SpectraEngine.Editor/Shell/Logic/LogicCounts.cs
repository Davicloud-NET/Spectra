namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>The numbers a status line reads off a scoped graph.</summary>
/// <param name="Cards">Cards shown, stubs included.</param>
/// <param name="Entities">Cards shown that are entities.</param>
/// <param name="Wires">Wires with at least one edge shown.</param>
/// <param name="WiresGoingNowhere">Of those, the ones that can never deliver.</param>
/// <param name="UnwiredEntities">Entities in the level that have no wires and so no card.</param>
/// <param name="IsTruncated">Whether the level has more entities than the snapshot listed.</param>
public readonly record struct LogicCounts(
    int Cards,
    int Entities,
    int Wires,
    int WiresGoingNowhere,
    int UnwiredEntities,
    bool IsTruncated);
