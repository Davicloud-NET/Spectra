using System;

namespace SpectraEngine.Core.Inspection;

/// <summary>One line of an entity's live state, short enough for a card.</summary>
/// <param name="NodeId">The entity's node.</param>
/// <param name="Label">What the value is, such as "opening".</param>
/// <param name="Value">The value, such as "14 of 39 ticks".</param>
public readonly record struct LogicEntityState(Guid NodeId, string Label, string Value);
