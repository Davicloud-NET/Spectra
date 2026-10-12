using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Core.Entities;

// The kinds of entry the play journal holds for how the player moves. The
// tuning outlives the level, so something new a level can change on it is
// one more of these.
internal static class TuningJournalKind
{
    public static readonly JournalKind<CharacterTuning, float> WalkSpeed = new(
        static tuning => tuning.WalkSpeed,
        static (tuning, first) => tuning.WalkSpeed = first);

    public static readonly JournalKind<CharacterTuning, float> JumpHeight = new(
        static tuning => tuning.JumpHeight,
        static (tuning, first) => tuning.JumpHeight = first);
}
