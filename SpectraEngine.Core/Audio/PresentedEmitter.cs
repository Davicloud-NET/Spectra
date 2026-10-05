using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using System.Numerics;

namespace SpectraEngine.Core.Audio;

// What the presenter keeps for one playing sound, from the frame it first
// sees it until the sound leaves the registry.
internal struct PresentedEmitter
{
    // The sound as its registry had it at the last read.
    public SoundEmitter Emitter;

    // The loaded sound, once it has been asked for.
    public LevelSound? Sound;

    // Null while the sound has no source.
    public AudioVoice? Voice;

    // Seconds the voice has played that the level's ticks have not counted.
    // Means nothing without a voice.
    public float VoiceLead;

    public SoundPathSmoother Smoother;

    // What is worked out for the sound: its own switches, less the ones the
    // engine has off. As of the last frame it could be heard.
    public SoundSimulation Simulated;

    public DopplerShift Doppler;

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

    public CaptionProgress Captions;

    // Whether the sound is heard from where it stands. OpenAL places only
    // mono sounds, so a stereo one plays at the listener whatever its switch
    // says. Until the sound is loaded, the switch alone decides.
    public readonly bool IsPlaced =>
        (Simulated & SoundSimulation.Placed) != 0 && Sound is not { IsStereo: true };
}
