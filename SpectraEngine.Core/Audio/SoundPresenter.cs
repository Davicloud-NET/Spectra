using SpectraEngine.Core.Assets;
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
// The simulation never hears of this class. It goes on counting a sound that
// has no source, which is how a loop comes back in step.
// A voice is lined up with the simulation when it starts and left alone
// after. The device and the tick count drift apart after a hitch, and a
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

    /// <summary>A listener that moves further than this in one frame has jumped, and nothing is smoothed.</summary>
    public const float ListenerJumpDistance = 8f;

    private readonly AudioManager _audio;
    private readonly ISoundPropagation _propagation;
    private readonly LevelVoices _voices;

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
    public SoundPresenter(AudioManager audio, AssetManager assets, ISoundPropagation propagation)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(propagation);

        _audio = audio;
        _propagation = propagation;
        _voices = new LevelVoices(audio, assets);
    }

    /// <summary>What the last <see cref="Update"/> made of the level's sounds.</summary>
    public SoundStats Stats { get; private set; }

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

        ReadOnlySpan<SoundEmitter> playing = registry.Playing;
        Reconcile(playing);

        var listener = new SoundListener(_audio.ListenerPosition, _audio.ListenerForward, _audio.ListenerUp);
        bool jumped = HasJumped(listener.Position);

        int asked = Ask(world, playing);
        _propagation.Resolve(in listener, _queries.AsSpan(0, asked), _paths.AsSpan(0, asked));
        Hear(playing, asked, deltaSeconds, jumped);

        _voices.HandOut(world, playing, _presented.AsSpan(0, _count));
        Stats = Configure(playing);
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
        Stats = default;
    }

    // Both lists are in id order and a new sound has the highest id, so one
    // pass drops what stopped and adds what started.
    private void Reconcile(ReadOnlySpan<SoundEmitter> playing)
    {
        Reserve(playing.Length);

        int read = 0;
        int write = 0;
        for (int i = 0; i < playing.Length; i++)
        {
            int id = playing[i].Id;
            while (read < _count && _presented[read].Id < id)
                _voices.Release(ref _presented[read++]);

            _presented[write++] = read < _count && _presented[read].Id == id
                ? _presented[read++]
                : new PresentedEmitter { Id = id };
        }

        while (read < _count)
            _voices.Release(ref _presented[read++]);

        if (write < _count)
            Array.Clear(_presented, write, _count - write);

        _count = write;
    }

    // Writes a query for every sound that can be heard at all and takes the
    // source from every sound that cannot. Returns how many it wrote.
    private int Ask(EntityWorld world, ReadOnlySpan<SoundEmitter> playing)
    {
        long tick = world.TickNumber;
        int asked = 0;

        for (int i = 0; i < _count; i++)
        {
            ref PresentedEmitter presented = ref _presented[i];
            ref readonly SoundEmitter emitter = ref playing[i];

            // Not started again: the device may be ahead of the ticks, and a
            // second start would play the end of the sound twice.
            if (_voices.TryDropEnded(ref presented))
                presented.HasPlayedOut = true;

            if (!CanBeHeard(world, in emitter, in presented, tick))
            {
                _voices.Release(ref presented);
                presented.Loudness = 0f;
                presented.Smoother.Reset();
                continue;
            }

            _queries[asked] = new SoundQuery(emitter.Node.WorldPosition, emitter.MinDistance, emitter.MaxDistance);
            _asked[asked++] = i;
        }

        return asked;
    }

    // A deleted node is out of the scene until an undo restores it. A sound
    // that plays once stays listed past its end until its owner stops it.
    private static bool CanBeHeard(
        EntityWorld world, in SoundEmitter emitter, in PresentedEmitter presented, long tick) =>
        !presented.IsUnplayable
        && !presented.HasPlayedOut
        && ReferenceEquals(emitter.Node.Owner, world.Scene)
        && !emitter.HasEndedAt(tick);

    private void Hear(ReadOnlySpan<SoundEmitter> playing, int asked, float deltaSeconds, bool jumped)
    {
        for (int k = 0; k < asked; k++)
        {
            ref PresentedEmitter presented = ref _presented[_asked[k]];

            // Only the first path is played. A second one is dropped here.
            SoundPath path = _paths[k].Count > 0 ? _paths[k][0] : new SoundPath(_queries[k].Position, 0f, 1f);

            presented.Smoother.Step(in path, deltaSeconds, jumped);
            presented.Position = path.Position;
            presented.Loudness = playing[_asked[k]].Gain * presented.Smoother.Gain;
        }
    }

    // Moves every voice to where its sound is now, and counts.
    private SoundStats Configure(ReadOnlySpan<SoundEmitter> playing)
    {
        int withoutSource = 0;
        int unplayable = 0;

        for (int i = 0; i < _count; i++)
        {
            ref PresentedEmitter presented = ref _presented[i];

            if (presented.Voice is { } voice)
                voice.Configure(LevelVoices.SettingsFor(in presented, in playing[i]));
            else if (presented.Loudness > SilenceGain)
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
