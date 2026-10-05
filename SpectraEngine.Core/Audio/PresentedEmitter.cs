using SpectraEngine.Core.Audio.Propagation;
using System.Numerics;

namespace SpectraEngine.Core.Audio;

// What the presenter keeps for one playing sound, from the frame it first
// sees it until the sound leaves the registry.
internal struct PresentedEmitter
{
    // The emitter's id in its registry.
    public int Id;

    // The loaded sound, once it has been asked for.
    public LevelSound? Sound;

    // Null while the sound has no source.
    public AudioVoice? Voice;

    public SoundPathSmoother Smoother;

    // Where the sound seems to come from this frame.
    public Vector3 Position;

    // The emitter's gain times its path's smoothed gain. Zero for a sound
    // that cannot be heard at all.
    public float Loudness;

    // The sound could not be loaded, or its file is not the one the level counted.
    public bool IsUnplayable;

    // Its voice ended without being told to. It is not started again.
    public bool HasPlayedOut;

    // The device had no source for it. Counted once, not once a frame.
    public bool WasRefused;
}
