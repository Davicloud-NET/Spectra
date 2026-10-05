using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Inspection;

/// <summary>One entity as a wiring graph shows it: who it is and the wires that leave it.</summary>
/// <param name="NodeId">The node the entity is on.</param>
/// <param name="Name">The node's name, which is what a wire's target matches.</param>
/// <param name="ClassName">The entity's class, as the map spells it.</param>
/// <param name="Wires">The wires in authored order. A wire is named by its index here.</param>
public readonly record struct LogicEntityInfo(
    Guid NodeId,
    string Name,
    string ClassName,
    IReadOnlyList<EntityConnection> Wires);
