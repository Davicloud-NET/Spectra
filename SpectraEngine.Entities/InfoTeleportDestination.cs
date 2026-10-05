using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities;

/// <summary>
/// A point for a <see cref="TriggerTeleport"/> to send the player to. It does
/// nothing by itself.
/// </summary>
// The player arrives with its feet at the node's origin, facing along the
// node's local +Z. A teleport finds it by name, so any entity can stand in.
[SpectraEntity("info_teleport_destination", Group = "Triggers", Placement = EntityPlacement.Point)]
public sealed partial class InfoTeleportDestination : Entity
{
}
