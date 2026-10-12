using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Core.Entities;

// A player whose way of moving a running level can change. The tuning
// outlives the level, so IPlayerPresence, which every entity gets, does not
// hand it out. PlayerWrites reaches it here and records the first value.
internal interface ITunedPlayer
{
    CharacterTuning Tuning { get; }
}
