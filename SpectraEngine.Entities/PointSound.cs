using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Entities;

/// <summary>
/// Plays a sound from where it stands: a hum, an alarm, a spoken line.
/// </summary>
// Where the sound is in its playback is counted in ticks from the sound's own
// length, never asked of an audio device, so a server ends it on the same tick.
// Play on a playing sound starts it over. Stop fires nothing.
// A marker fires OnMarker with its name. A looped sound fires its markers on
// every pass, and one at the loop's end fires as the pass ends.
// Looped repeats the file's loop region, or the whole sound when it has none.
// Not looped plays once through and ignores the region.
// The sound is its own activator for OnEnded and OnMarker.
[SpectraEntity("point_sound", Display = "Sound", Group = "Sound", Placement = EntityPlacement.Point)]
public sealed partial class PointSound : Entity
{
    /// <summary>The slowest a sound plays.</summary>
    public const float MinimumPitch = 0.1f;

    /// <summary>The fastest a sound plays.</summary>
    // An audio device only resamples so far. Past that the tick count would
    // run ahead of what is heard.
    public const float MaximumPitch = 10f;

    /// <summary>Fired when a sound that is not looped reaches its end. Not by a stop.</summary>
    [EntityOutput]
    public const string OnEnded = nameof(OnEnded);

    /// <summary>Fired when playback reaches a marker, with the marker's name.</summary>
    [EntityOutput]
    public const string OnMarker = nameof(OnMarker);

    // What the catalog said at spawn. Means nothing unless _canPlay.
    private SoundDescription _description;
    private bool _canPlay;

    private int _emitter = SoundEmitters.None;

    // The pass of the loop the markers have been fired up to, and the first
    // frame of it not looked at yet.
    private long _pass;
    private long _nextFrame;

    /// <summary>The sound file to play, as a content path.</summary>
    [Keyvalue(
        "sound",
        Display = "Sound",
        Tooltip = "The sound file to play.",
        Type = KeyvalueType.AssetSound)]
    public string Sound { get; set; } = "";

    /// <summary>How loud the sound is before distance. 1 is the file as it is.</summary>
    [Keyvalue(
        "volume",
        Display = "Volume",
        Tooltip = "How loud the sound is. 1 is the file as it is, 0 is silent.",
        Default = "1",
        Min = 0f)]
    public float Volume { get; set; } = 1f;

    /// <summary>How fast the sound plays. 1 is the file as it is.</summary>
    [Keyvalue(
        "pitch",
        Display = "Pitch",
        Tooltip = "How fast the sound plays. 2 is twice as fast and an octave higher.",
        Default = "1",
        Min = MinimumPitch,
        Max = MaximumPitch)]
    public float Pitch { get; set; } = 1f;

    /// <summary>The sound is at full volume inside this distance.</summary>
    [Keyvalue(
        "mindistance",
        Display = "Minimum distance",
        Tooltip = "The sound is at full volume inside this distance.",
        Default = "2",
        Min = 0f)]
    public float MinDistance { get; set; } = 2f;

    /// <summary>The sound is silent beyond this distance.</summary>
    [Keyvalue(
        "maxdistance",
        Display = "Maximum distance",
        Tooltip = "The sound is silent beyond this distance.",
        Default = "30",
        Min = 0f)]
    public float MaxDistance { get; set; } = 30f;

    /// <summary>Whether the sound repeats until something stops it.</summary>
    [Keyvalue(
        "looped",
        Display = "Looped",
        Tooltip = "The sound repeats until something stops it. If the file has a loop region, that is what repeats.",
        Default = "0")]
    public bool IsLooped { get; set; }

    /// <summary>Whether the sound plays when the level starts.</summary>
    [Keyvalue(
        "startplaying",
        Display = "Start playing",
        Tooltip = "The sound plays when the level starts.",
        Default = "0")]
    public bool StartPlaying { get; set; }

    /// <summary>Whether the sound is playing.</summary>
    public bool IsPlaying => _emitter != SoundEmitters.None;

    /// <summary>How many inputs carried a number this sound could not use.</summary>
    public int RefusedInputCount { get; private set; }

    /// <inheritdoc/>
    protected override void OnSpawn()
    {
        if (!(Volume >= 0f))
        {
            RefuseAuthored("volume");
            Volume = 1f;
        }

        if (!IsPitch(Pitch))
        {
            RefuseAuthored("pitch");
            Pitch = 1f;
        }

        // Asked once, here: the answer loads the sound before anything plays
        // it, and a missing one is one warning, not one per Play.
        if (Sound.Length == 0)
            Warn("has no sound set, so it plays nothing");
        else if (World.SoundCatalog is not { } catalog)
            Warn($"cannot play '{Sound}': the level was started with no sound catalog");
        else if (!catalog.TryDescribe(Sound, out _description, out string reason))
            Warn($"cannot play '{Sound}': {reason}");
        else
            _canPlay = true;
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        if (StartPlaying)
            Start();
    }

    /// <inheritdoc/>
    protected override void OnRemove() => Halt();

    /// <inheritdoc/>
    protected override void OnTick()
    {
        // Gone when something other than this entity stopped it.
        if (!World.Sounds.TryGet(_emitter, out SoundEmitter emitter))
        {
            Halt();
            return;
        }

        SoundPosition now = emitter.PositionAt(World.TickNumber);
        if (now.Pass > _pass)
        {
            // A loop shorter than a tick can turn round more than once in
            // one. The passes in between fire nothing.
            FireMarkers(_nextFrame, emitter.Loop.EndFrame);
            FireMarkers(emitter.Loop.StartFrame, now.Frame);
            _pass = now.Pass;
        }
        else
        {
            FireMarkers(_nextFrame, now.Frame);
        }

        _nextFrame = now.Frame + 1;

        if (!emitter.HasEndedAt(World.TickNumber))
            return;

        Halt();
        FireOnEnded();
    }

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        bool playing = World.Sounds.TryGet(_emitter, out SoundEmitter emitter);
        SoundPosition at = playing ? emitter.PositionAt(World.TickNumber) : default;
        bool looping = playing && emitter.Loop.IsLooping;

        double seconds = _description.SecondsAt(at.Frame);
        double length = _description.SecondsAt(looping ? emitter.Loop.EndFrame : _description.FrameCount);

        if (!_canPlay)
            state.Headline("state", "no sound");
        else if (!playing)
            state.Headline("state", "stopped");
        else
            state.Headline(looping ? "looping" : "playing", HeadlineText.SecondsOf(seconds, length));

        state.Add("playing", playing);
        state.Add("seconds in", (float)Math.Round(seconds, 3));
        state.Add("seconds long", (float)Math.Round(length, 3));
        state.Add("times round", (int)at.Pass);
        state.Add("volume", Volume);
        state.Add("pitch", Pitch);
        state.Add("refused inputs", RefusedInputCount);
    }

    [EntityInput("Play")]
    private void Play(ref EntityInputContext context) => Start();

    [EntityInput("Stop")]
    private void Stop(ref EntityInputContext context) => Halt();

    // From now on, and for every later Play.
    [EntityInput("SetVolume")]
    private void SetVolume(ref EntityInputContext context)
    {
        if (!KeyvalueWire.TryParseFloat(context.Parameter, out float volume) || volume < 0f)
        {
            RefusedInputCount++;
            return;
        }

        Volume = volume;
        World.Sounds.SetGain(_emitter, volume);
    }

    // What has played so far stays played: the end moves by what is left.
    [EntityInput("SetPitch")]
    private void SetPitch(ref EntityInputContext context)
    {
        if (!KeyvalueWire.TryParseFloat(context.Parameter, out float pitch) || !IsPitch(pitch))
        {
            RefusedInputCount++;
            return;
        }

        Pitch = pitch;
        World.Sounds.SetPitch(_emitter, pitch);
    }

    // Written so NaN is not one.
    private static bool IsPitch(float pitch) => pitch >= MinimumPitch && pitch <= MaximumPitch;

    private void Start()
    {
        if (!_canPlay)
            return;

        World.Sounds.Stop(_emitter);
        _emitter = World.Sounds.Play(
            Node, Sound, in _description, new SoundEmitterSettings(Volume, Pitch, MinDistance, MaxDistance, IsLooped));

        _pass = 0;
        _nextFrame = 0;
        SetTicking(true);
    }

    private void Halt()
    {
        World.Sounds.Stop(_emitter);
        _emitter = SoundEmitters.None;
        SetTicking(false);
    }

    // Both ends count: a marker on frame 0 fires as the sound starts, and
    // one at the very end fires as it ends.
    private void FireMarkers(long first, long last)
    {
        IReadOnlyList<AudioMarker> markers = _description.Markers;
        for (int i = 0; i < markers.Count; i++)
        {
            AudioMarker marker = markers[i];
            if (marker.Frame >= first && marker.Frame <= last)
                FireOnMarker(parameterOverride: marker.Name);
        }
    }

    private void RefuseAuthored(string key)
    {
        Data.TryGetValue(key, out string authored);
        RefuseKeyvalue(key, authored);
    }
}
