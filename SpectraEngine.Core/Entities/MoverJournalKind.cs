using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Core.Entities;

// The entry the play journal holds for the player's mover. A mover a level
// installed belongs to that level's scripts, which Stop closes.
internal static class MoverJournalKind
{
    public static readonly JournalKind<IMovedPlayer, ICharacterMover> Mover = new(
        static player => player.Mover,
        static (player, first) => player.Mover = first);
}
