using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Core.Entities;

// A player whose mover a running level can replace. The mover outlives the
// level as the tuning does, so IPlayerPresence does not hand it out.
// PlayerWrites reaches it here and records the one the level started with.
internal interface IMovedPlayer
{
    ICharacterMover Mover { get; set; }
}
