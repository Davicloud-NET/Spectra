using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using System;
using System.Numerics;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// Makes a running level's sounds audible. Once a frame it reads what the
/// level is playing, works out how each sound reaches the listener and gives
/// the audio device's sources to the loudest. Render thread only.
/// </summary>
// The simulation goes on counting a sound that has no source, which is how
// a loop comes back in step. A voice is lined up with the ticks when it
// starts and never after: the two clocks drift apart after a hitch, and a
// voice that chased the ticks would be heard doing it.
public sealed class SoundPresenter
{
    /// <summary>A sound at or below this loudness needs no source. 60 dB down.</summary>
    public const float SilenceGain = 0.001f;

    /// <summary>
    /// How many times louder a sound without a source must be than the
    /// quietest one that has one before it takes that source.
    /// </summary>
    // Without it two equally loud sounds would swap every frame.
    public const float ChallengeMargin = 1.5f;

    /// <summary>
    /// A sound that started this many seconds ago or less is played from its
    /// first frame. An older one starts where the simulation says it is.
    /// </summary>
    // A frame can run several ticks, and skipping them would cut off the attack.
    public const float FreshStartSeconds = 0.1f;

    /// <summary>
    /// How many seconds a sound that plays once may go on after the simulation
    /// has finished it. Past that it is cut.
    /// </summary>
    // The device starts a sound a frame after its tick at best, and up to
    // FreshStartSeconds behind it, so it reaches the end that much later.
    public const float TailSeconds = 0.25f;

    /// <summary>A listener that moves further than this in one frame has jumped, and nothing is smoothed.</summary>
    public const float ListenerJumpDistance = 8f;

    private readonly AudioManager _audio;
    private readonly ISoundPropagation _propagation;
    private readonly LevelVoices _voices;
    private readonly CaptionTracker _captions;

    // The registry read last frame. Another one is another level.
    private SoundEmitters? _registry;

    // One for each playing sound, in the registry's order.
    private PresentedEmitter[] _presented = [];
    private int _count;

    // One frame's questions and answers, and which sound each is for.
    private SoundQuery[] _queries = [];
    private SoundPaths[] _paths = [];
    private int[] _asked = [];

    private Vector3 _listenerPosition;
    private bool _hasListener;

    /// <summary>Builds a presenter that plays on <paramref name="audio"/>.</summary>
    /// <param name="assets">Where the level's sounds are loaded from.</param>
    /// <param name="propagation">Decides how each sound reaches the listener.</param>
    /// <param name="captions">Filled each frame with the captions of the sounds that are heard.</param>
    /// <param name="logger">Told once about each sound that cannot be played.</param>
    public SoundPresenter(
        AudioManager audio, AssetManager assets, ISoundPropagation propagation, CaptionFeed captions, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(propagation);
        ArgumentNullException.ThrowIfNull(captions);
        ArgumentNullException.ThrowIfNull(logger);

        _audio = audio;
        _propagation = propagation;
        _voices = new LevelVoices(audio, assets, logger);
        _captions = new CaptionTracker(captions);
        Captions = captions;
    }

    /// <summary>What the last <see cref="Update"/> made of the level's sounds.</summary>
    public SoundStats Stats { get; private set; }

    /// <summary>The captions of the sounds that are heard, as of the last <see cref="Update"/>.</summary>
    public CaptionFeed Captions { get; }

    /// <summary>
    /// Brings the device's voices in line with what the level is playing.
    /// Call once a frame, after the listener has been placed for the frame.
    /// </summary>
    /// <param name="world">The running level, or null when none is.</param>
    /// <param name="deltaSeconds">The frame's length, for smoothing.</param>
    public void Update(EntityWorld? world, float deltaSeconds)
    {
        SoundEmitters? registry = world is { IsActive: true } ? world.Sounds : null;
        if (!ReferenceEquals(registry, _registry))
        {
            Forget();
            _registry = registry;
        }

        if (world is null || registry is null)
            return;

        _voices.ExpireTails(deltaSeconds);
        Reconcile(registry.Playing, world.TickNumber);

        var listener = new SoundListener(_audio.ListenerPosition, _audio.ListenerForward, _audio.ListenerUp);
        bool jumped = HasJumped(listener.Position);

        int asked = QueryAudible(world);
        _propagation.Resolve(in listener, _queries.AsSpan(0, asked), _paths.AsSpan(0, asked));
        Hear(asked, deltaSeconds, jumped);

        _voices.GiveSourcesToLoudest(world, _presented.AsSpan(0, _count));
        ConfigureVoices();
        _captions.Update(world, _presented.AsSpan(0, _count), _voices, deltaSeconds);
        Stats = CountSounds();
    }

    // Stop, a map load during play and a save all end the world, so this one
    // rule covers the three.
    private void Forget()
    {
        for (int i = 0; i < _count; i++)
            _voices.Release(ref _presented[i]);

        Array.Clear(_presented, 0, _count);
        _count = 0;
        _hasListener = false;
        _voices.Clear();
        _captions.EndLevel();
        Stats = default;
    }

    // Both lists are in id order and a new sound has the highest id, so one
    // pass drops what stopped and adds what started.
    private void Reconcile(ReadOnlySpan<SoundEmitter> playing, long tick)
    {
        Reserve(playing.Length);

        int read = 0;
        int write = 0;
        for (int i = 0; i < playing.Length; i++)
        {
            int id = playing[i].Id;
            while (read < _count && _presented[read].Emitter.Id < id)
                Drop(ref _presented[read++], tick);

            _presented[write] = read < _count && _presented[read].Emitter.Id == id ? _presented[read++] : default;
            _presented[write++].Emitter = playing[i];
        }

        while (read < _count)
            Drop(ref _presented[read++], tick);

        if (write < _count)
            Array.Clear(_presented, write, _count - write);

        _count = write;
    }

    // A sound that left the registry at its end was not cut short there, so
    // its voice is not cut short here. One that was stopped is.
    private void Drop(ref PresentedEmitter gone, long tick)
    {
        if (gone.Emitter.HasEndedAt(tick))
            _voices.LetPlayOut(ref gone);
        else
            _voices.Release(ref gone);
    }

    private int QueryAudible(EntityWorld world)
    {
        long tick = world.TickNumber;
        int asked = 0;

        for (int i = 0; i < _count; i++)
        {
            ref PresentedEmitter presented = ref _presented[i];
            LetGoOfEndedVoice(ref presented, tick);

            if (!CanBeHeard(world, in presented, tick))
            {
                Silence(ref presented);
                continue;
            }

            ref readonly SoundEmitter emitter = ref presented.Emitter;
            _queries[asked] = new SoundQuery(emitter.Node.WorldPosition, emitter.MinDistance, emitter.MaxDistance);
            _asked[asked++] = i;
        }

        return asked;
    }

    private void LetGoOfEndedVoice(ref PresentedEmitter presented, long tick)
    {
        // Not started again: the device may be ahead of the ticks, and a
        // second start would play the end of the sound twice.
        if (_voices.TryDropEnded(ref presented))
            presented.HasPlayedOut = true;

        // Still listed past its end, until its owner stops it.
        if (presented.Emitter.HasEndedAt(tick))
            _voices.LetPlayOut(ref presented);
    }

    // A deleted node is out of the scene until an undo restores it.
    private static bool CanBeHeard(EntityWorld world, in PresentedEmitter presented, long tick) =>
        !presented.IsUnplayable
        && !presented.HasPlayedOut
        && ReferenceEquals(presented.Emitter.Node.Owner, world.Scene)
        && !presented.Emitter.HasEndedAt(tick);

    private void Silence(ref PresentedEmitter presented)
    {
        _voices.Release(ref presented);
        presented.Loudness = 0f;
        presented.Smoother.Reset();
    }

    private void Hear(int asked, float deltaSeconds, bool jumped)
    {
        for (int k = 0; k < asked; k++)
        {
            ref PresentedEmitter presented = ref _presented[_asked[k]];

            // Only the first path is played. A second one is dropped here.
            SoundPath path = _paths[k].Count > 0 ? _paths[k][0] : new SoundPath(_queries[k].Position, 0f, 1f);

            // The device refuses a place that is not a number. Such a path is
            // silent, and a voice fades out where it last was.
            if (IsFinite(path.Position))
                presented.Position = path.Position;
            else
                path = path with { Gain = 0f };

            presented.Smoother.Step(in path, deltaSeconds, jumped);

            // The device plays no source above full volume, so none ranks above it.
            presented.Loudness = MathF.Min(presented.Emitter.Gain * presented.Smoother.Gain, 1f);
        }
    }

    private static bool IsFinite(Vector3 place) =>
        float.IsFinite(place.X) && float.IsFinite(place.Y) && float.IsFinite(place.Z);

    private void ConfigureVoices()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_presented[i].Voice is { } voice)
                voice.Configure(LevelVoices.SettingsFor(in _presented[i]));
        }
    }

    private SoundStats CountSounds()
    {
        int withoutSource = 0;
        int unplayable = 0;

        for (int i = 0; i < _count; i++)
        {
            ref readonly PresentedEmitter presented = ref _presented[i];

            if (presented.Voice is null && presented.Loudness > SilenceGain)
                withoutSource++;

            if (presented.IsUnplayable)
                unplayable++;
        }

        return new SoundStats(_count, _voices.Count, withoutSource, _voices.RefusedStarts, unplayable);
    }

    private bool HasJumped(Vector3 position)
    {
        bool jumped = _hasListener
            && Vector3.DistanceSquared(position, _listenerPosition) > ListenerJumpDistance * ListenerJumpDistance;

        _listenerPosition = position;
        _hasListener = true;
        return jumped;
    }

    private void Reserve(int count)
    {
        if (count <= _presented.Length)
            return;

        int size = Math.Max(count, Math.Max(16, _presented.Length * 2));
        Array.Resize(ref _presented, size);
        _queries = new SoundQuery[size];
        _paths = new SoundPaths[size];
        _asked = new int[size];
    }
}
