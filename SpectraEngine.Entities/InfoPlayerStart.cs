using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities;

/// <summary>
/// Where the player starts. A level uses the first one it has. The player
/// stands on the node and faces along its local +Z.
/// </summary>
// Nothing to set and nothing to send: the engine reads the node.
[SpectraEntity(
    "info_player_start",
    Display = "Player start",
    Group = "Player",
    Placement = EntityPlacement.Point)]
public sealed partial class InfoPlayerStart : Entity, IPlayerStart
{
}
