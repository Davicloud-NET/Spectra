using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The sounds playing in a level. Entities start, stop and change them here,
/// and whatever makes them audible reads them. It holds what is playing, not
/// how it sounds, and touches no audio device. Emptied when the world
/// deactivates. Render thread only.
/// </summary>
public sealed class SoundEmitters
{
    /// <summary>The id no playing sound has.</summary>
    public const int None = 0;

    private readonly EntityWorld _world;

    // In the order they started, which is also ascending id.
    private readonly List<SoundEmitter> _playing = [];

    private int _lastId;

    internal SoundEmitters(EntityWorld world) => _world = world;

    /// <summary>How many sounds are playing.</summary>
    public int Count => _playing.Count;

    /// <summary>
    /// Goes up whenever a sound starts, stops or changes. Two reads that match
    /// mean nothing here changed in between. A node that moves does not count:
    /// read each emitter's node for where it is. Every world counts from zero,
    /// so a reader that outlives a world also checks which registry it read.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>
    /// The playing sounds, oldest first. Good until the next sound starts or
    /// stops.
    /// </summary>
    public ReadOnlySpan<SoundEmitter> Playing => CollectionsMarshal.AsSpan(_playing);

    /// <summary>
    /// Starts a sound on a node, from its first frame, on the tick being run.
    /// A sound that plays once stays listed past its end, until whoever
    /// started it stops it. <see cref="SoundEmitter.HasEndedAt"/> says when
    /// that is.
    /// </summary>
    /// <param name="node">Where the sound sits. It follows the node.</param>
    /// <param name="path">The authored sound's content path.</param>
    /// <param name="sound">What the world's <see cref="ISoundCatalog"/> said about that path.</param>
    /// <returns>The id to stop or change it by.</returns>
    /// <exception cref="InvalidOperationException">The world is not active.</exception>
    public int Play(SceneNode node, string path, in SoundDescription sound, in SoundEmitterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfNotGain(settings.Gain);
        ThrowIfNotPitch(settings.Pitch);

        if (!_world.IsActive)
            throw new InvalidOperationException("Play on an entity world that is not active.");

        long tick = _world.TickNumber;
        var emitter = new SoundEmitter
        {
            Id = ++_lastId,
            Node = node,
            Path = path,
            Gain = settings.Gain,
            Pitch = settings.Pitch,
            MinDistance = settings.MinDistance,
            MaxDistance = settings.MaxDistance,
            IsLooped = settings.IsLooped,
            StartTick = tick,
            FrameCount = sound.FrameCount,
            SampleRate = sound.SampleRate,
            Loop = Repeated(in sound, settings.IsLooped),
            AnchorTick = tick,
            FramesPerTick = SoundEmitter.FramesInATick(_world.FixedDeltaTime, sound.SampleRate, settings.Pitch),
        };

        _playing.Add(emitter);
        Version++;
        return emitter.Id;
    }

    /// <summary>Stops a sound. False when no sound has that id, which is harmless.</summary>
    public bool Stop(int id)
    {
        int at = IndexOf(id);
        if (at < 0)
            return false;

        _playing.RemoveAt(at);
        Version++;
        return true;
    }

    /// <summary>Changes how loud a playing sound is. False when no sound has that id.</summary>
    public bool SetGain(int id, float gain)
    {
        ThrowIfNotGain(gain);

        int at = IndexOf(id);
        if (at < 0)
            return false;

        _playing[at] = _playing[at] with { Gain = gain };
        Version++;
        return true;
    }

    /// <summary>
    /// Changes how fast a playing sound plays, from the tick being run. What
    /// it has played so far stays played. False when no sound has that id.
    /// </summary>
    public bool SetPitch(int id, float pitch)
    {
        ThrowIfNotPitch(pitch);

        int at = IndexOf(id);
        if (at < 0)
            return false;

        _playing[at] = _playing[at].WithPitch(pitch, _world.TickNumber, _world.FixedDeltaTime);
        Version++;
        return true;
    }

    /// <summary>Finds a playing sound by its id.</summary>
    public bool TryGet(int id, out SoundEmitter emitter)
    {
        int at = IndexOf(id);
        emitter = at < 0 ? default : _playing[at];
        return at >= 0;
    }

    internal void Clear()
    {
        if (_playing.Count == 0)
            return;

        _playing.Clear();
        Version++;
    }

    // What each sound has played by the tick stays played. The rest is
    // counted at the new step.
    internal void OnStepChanged(long tick, float fixedDt)
    {
        if (_playing.Count == 0)
            return;

        for (int i = 0; i < _playing.Count; i++)
            _playing[i] = _playing[i].WithStep(tick, fixedDt);

        Version++;
    }

    // An undo of a delete rebuilds the node as a new object under its old id.
    internal void OnNodeAdded(SceneNode node)
    {
        bool moved = false;
        for (int i = 0; i < _playing.Count; i++)
        {
            SoundEmitter emitter = _playing[i];
            if (emitter.Node.Id != node.Id || ReferenceEquals(emitter.Node, node))
                continue;

            _playing[i] = emitter with { Node = node };
            moved = true;
        }

        if (moved)
            Version++;
    }

    // Ids only go up and a stop keeps the order, so the list is sorted by id.
    private int IndexOf(int id)
    {
        int low = 0;
        int high = _playing.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int found = _playing[middle].Id;
            if (found == id)
                return middle;

            if (found < id)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return -1;
    }

    // An empty sound has nothing to repeat, so it ends at once.
    private static LoopRegion Repeated(in SoundDescription sound, bool isLooped)
    {
        if (!isLooped || sound.FrameCount == 0)
            return LoopRegion.None;

        return sound.Loop.IsLooping ? sound.Loop : new LoopRegion(0, sound.FrameCount);
    }

    // Written so NaN is refused too.
    private static void ThrowIfNotGain(float gain)
    {
        if (!(gain >= 0f) || float.IsPositiveInfinity(gain))
            throw new ArgumentOutOfRangeException(nameof(gain), gain, "A gain is a number from zero up.");
    }

    private static void ThrowIfNotPitch(float pitch)
    {
        if (!(pitch > 0f) || float.IsPositiveInfinity(pitch))
            throw new ArgumentOutOfRangeException(nameof(pitch), pitch, "A pitch is a number above zero.");
    }
}
