using System;

namespace SpectraEngine.Core.Inspection;

/// <summary>One script of a level as a wiring graph shows it: a script that runs when the level plays.</summary>
/// <param name="NodeId">The script's node.</param>
/// <param name="Name">The node's name.</param>
/// <param name="EntityId">
/// The entity the script is inside: the nearest node above it that carries
/// one. Empty when there is none.
/// </param>
public readonly record struct LogicScriptInfo(Guid NodeId, string Name, Guid EntityId);
