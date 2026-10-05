using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// One sound playing in a level: where it sits, how it is played and when it
/// started. Where it is in its playback is counted in ticks from these
/// numbers, so it is the same with or without an audio device.
/// </summary>
public readonly struct SoundEmitter
{
    // A float step is a hair off the tick length it stands for. Without one
    // part in a million more, a second of sound ends a tick late at some
    // tick rates.
    private const double WholeTickSlack = 1e-6;

    /// <summary>
    /// Names this playing sound for as long as it plays. Its world never gives
    /// the number out again, so a sound that is played anew has a new one.
    /// Another world starts at 1 again: the number means nothing outside the
    /// registry it came from.
    /// </summary>
    public int Id { get; internal init; }

    /// <summary>
    /// The node the sound sits on. The node's world position is where the
    /// sound is. A node that is deleted is out of the scene until an undo
    /// restores it, and the sound is then on the restored node.
    /// </summary>
    public SceneNode Node { get; internal init; }

    /// <summary>The authored sound's content path, such as <c>Sounds/door_open.wav</c>.</summary>
    public string Path { get; internal init; }

    /// <summary>Linear loudness before distance. 1 is the file as it is.</summary>
    public float Gain { get; internal init; }

    /// <summary>Playback speed. 1 is the file as it is, 2 is twice as fast.</summary>
    public float Pitch { get; internal init; }

    /// <summary>The sound is at full volume inside this distance.</summary>
    public float MinDistance { get; internal init; }

    /// <summary>The sound is silent beyond this distance.</summary>
    public float MaxDistance { get; internal init; }

    /// <summary>Whether the sound was asked to repeat until it is stopped.</summary>
    public bool IsLooped { get; internal init; }

    /// <summary>
    /// What is worked out for the sound on its way to the listener. It changes
    /// what is heard and nothing the level counts.
    /// </summary>
    public SoundSimulation Simulated { get; internal init; }

    /// <summary>The tick the sound started on. Zero for one that started while the level spawned.</summary>
    public long StartTick { get; internal init; }

    /// <summary>The sound's length in sample frames.</summary>
    public long FrameCount { get; internal init; }

    /// <summary>The sound's sample frames a second.</summary>
    public int SampleRate { get; internal init; }

    /// <summary>
    /// The frames that repeat: the file's loop region, or the whole sound when
    /// it has none. <see cref="LoopRegion.None"/> for a sound that plays once.
    /// </summary>
    public LoopRegion Loop { get; internal init; }

    // Frames played by AnchorTick, and frames a tick from there on. A change
    // of pitch or of the world's step moves the anchor, so what was played
    // stays played.
    internal long AnchorTick { get; init; }

    internal double AnchorFrames { get; init; }

    internal double FramesPerTick { get; init; }

    /// <summary>
    /// Sample frames played by a tick, counting every pass of a loop: ticks
    /// since the start, times the tick length, the sample rate and the pitch.
    /// </summary>
    public long FramesPlayedAt(long tick) => (long)Math.Floor(FramesAt(tick));

    /// <summary>Where in its file the sound is at a tick.</summary>
    public SoundPosition PositionAt(long tick)
    {
        long played = FramesPlayedAt(tick);

        if (!Loop.IsLooping)
            return new SoundPosition(0, Math.Min(played, FrameCount));
        if (played < Loop.EndFrame)
            return new SoundPosition(0, played);

        long pass = (played - Loop.StartFrame) / Loop.LengthFrames;
        return new SoundPosition(pass, played - (pass * Loop.LengthFrames));
    }

    /// <summary>
    /// Whether a sound that plays once has reached its end by a tick. Never
    /// true for one that repeats.
    /// </summary>
    public bool HasEndedAt(long tick) => !Loop.IsLooping && FramesPlayedAt(tick) >= FrameCount;

    internal static double FramesInATick(float fixedDt, int sampleRate, float pitch) =>
        (double)fixedDt * sampleRate * pitch * (1d + WholeTickSlack);

    internal SoundEmitter WithPitch(float pitch, long tick, float fixedDt) => this with
    {
        Pitch = pitch,
        AnchorTick = tick,
        AnchorFrames = FramesAt(tick),
        FramesPerTick = FramesInATick(fixedDt, SampleRate, pitch),
    };

    internal SoundEmitter WithStep(long tick, float fixedDt) => WithPitch(Pitch, tick, fixedDt);

    private double FramesAt(long tick) => AnchorFrames + (Math.Max(tick - AnchorTick, 0L) * FramesPerTick);
}
