using System;
using System.Numerics;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Diagnostics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// Propagation through walls: a sound is as loud as its distance says, times
/// what the solids between it and the listener let through, and as dull as
/// they make it. Each solid costs what its material takes at its thickness.
/// What a sound is part of is not in its way, by the rule of
/// <see cref="ISoundObstacles.Trace"/>. Any other solid the sound or the
/// listener is inside counts by how deep that end is in it. A list of solids
/// that ran out of room counts for what it holds. Render thread only.
/// </summary>
// A sound is heard along several lines, to the listener and to a ring round
// the listener's head, and their gains are averaged: an opening that half the
// lines pass lets half the sound through. Averaged in decibels, a view half
// open beside a thick wall would sound nearly shut, and a real gap leaks far
// more than that.
public sealed class WallPropagation : ISoundPropagation
{
    /// <summary>A sound at or below this gain from distance alone is not traced. 60 dB down.</summary>
    public const float InaudibleGain = SoundPresenter.SilenceGain;

    /// <summary>
    /// A listener or a sound that is further than this from where it last
    /// was has jumped. Its old answer is dropped, not kept until its turn.
    /// </summary>
    public const float JumpDistance = SoundPresenter.ListenerJumpDistance;

    // More solids than this on one line are past the most walls can take.
    private const int MaxSpans = 16;

    // What a sound's turn is ranked by, most urgent first: one with no answer,
    // one traced along a single line so far, one whose answer has grown old,
    // and one whose world, listener or place changed. Within the first, second
    // and last the louder goes first, within the third the older.
    private const double NewSound = 3000;
    private const double OneLine = 2000;
    private const double GrownOld = 1000;
    private const double OldestCounted = 900;

    private readonly ISoundObstacles _world;
    private readonly IAcousticMaterials _materials;
    private readonly WallPropagationSettings _settings;
    private readonly TimeProvider _clock;
    private readonly double _secondsPerTick;

    private readonly DirectPropagation _distance = new();
    private readonly HeadLines _lines;
    private readonly WallAnswers _answers = new();
    private readonly SolidSpan[] _spans = new SolidSpan[MaxSpans];

    // Per sound of the frame: its answer's slot, or -1 where walls do not count.
    private int[] _slots = [];

    // The sounds that are due, and how urgent each is, negated for the sort.
    private int[] _due = [];
    private double[] _urgency = [];
    private int _newSounds;

    private Vector3 _listener;
    private bool _hasListener;

    /// <summary>Builds a propagation over a world and its materials.</summary>
    /// <param name="world">Traces what stands between two points.</param>
    /// <param name="materials">Says what each solid is made of.</param>
    /// <param name="settings">The numbers to run on, or null for <see cref="WallPropagationSettings.Default"/>.</param>
    /// <param name="clock">What an answer's age is read from, or null for the system's.</param>
    public WallPropagation(
        ISoundObstacles world,
        IAcousticMaterials materials,
        WallPropagationSettings? settings = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        _settings = settings ?? WallPropagationSettings.Default;
        ArgumentOutOfRangeException.ThrowIfLessThan(_settings.Lines, 1, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfLessThan(_settings.TracesPerFrame, _settings.Lines, nameof(settings));

        _world = world;
        _materials = materials;
        _clock = clock ?? TimeProvider.System;
        _secondsPerTick = 1d / _clock.TimestampFrequency;
        _lines = new HeadLines(_settings.Lines, _settings.HeadRadius);
    }

    /// <summary>Times the tracing under <see cref="FramePhase.SoundWalls"/> when set.</summary>
    public FrameProfiler? Profiler { get; set; }

    /// <summary>What the last <see cref="Resolve"/> did.</summary>
    public WallPropagationStats Stats { get; private set; }

    /// <summary>The numbers this propagation runs on.</summary>
    public WallPropagationSettings Settings => _settings;

    /// <inheritdoc/>
    public void Resolve(in SoundListener listener, ReadOnlySpan<SoundQuery> emitters, Span<SoundPaths> results)
    {
        _distance.Resolve(in listener, emitters, results);

        using FrameProfiler.Scope timing =
            Profiler is { } profiler ? profiler.Measure(FramePhase.SoundWalls) : default;

        if (!_world.TryReadWorld(out long revision))
        {
            _answers.Clear();
            _hasListener = false;
            Stats = default;
            return;
        }

        if (HasJumped(listener.Position))
            _answers.Clear();

        var frame = new Frame(listener.Position, revision, _clock.GetTimestamp());

        Reserve(emitters.Length);
        int due = FindDue(in frame, emitters, results);
        Stats = Spend(in frame, emitters, due);
        Apply(results[..emitters.Length]);
    }

    // The one place a sound says whether walls count for it.
    private static bool WallsCountFor(in SoundQuery sound) => true;

    // Gives every sound that walls count for its answer from last frame, or
    // an empty one, and lists the ones whose answer is due, most urgent first.
    private int FindDue(in Frame frame, ReadOnlySpan<SoundQuery> emitters, ReadOnlySpan<SoundPaths> results)
    {
        _answers.Begin(emitters.Length);
        _newSounds = 0;
        int due = 0;

        for (int i = 0; i < emitters.Length; i++)
        {
            _slots[i] = -1;

            // Written so a NaN gain is not traced either.
            if (results[i].Count == 0 || !(results[i][0].Gain > InaudibleGain) || !WallsCountFor(in emitters[i]))
                continue;

            int slot = _answers.Claim(emitters[i].Body?.Id ?? Guid.Empty);
            _slots[i] = slot;

            double urgency = Urgency(in frame, emitters[i].Position, ref _answers[slot], results[i][0].Gain);
            if (urgency <= 0)
                continue;

            _urgency[due] = -urgency;
            _due[due++] = i;
        }

        _urgency.AsSpan(0, due).Sort(_due.AsSpan(0, due));
        return due;
    }

    // Zero for an answer that still holds.
    private double Urgency(in Frame frame, Vector3 sound, ref WallAnswer answer, float gain)
    {
        if (answer.Lines > 0 && IsFurther(answer.Sound, sound, JumpDistance))
            answer.Lines = 0;

        if (answer.Lines == 0)
        {
            _newSounds++;
            return NewSound + gain;
        }

        if (answer.Lines < _lines.Count)
            return OneLine + gain;

        double age = (frame.Now - answer.TracedAt) * _secondsPerTick;
        if (age >= _settings.RefreshSeconds)
            return GrownOld + Math.Min(age, OldestCounted);

        bool changed = answer.Revision != frame.Revision
            || IsFurther(answer.Listener, frame.Listener, _settings.MoveDistance)
            || IsFurther(answer.Sound, sound, _settings.MoveDistance);

        return changed ? gain : 0;
    }

    // Traces the due sounds in turn until the frame's lines are spent. A new
    // sound is answered at once. When the new ones do not all fit along every
    // line, the quieter get the one line to the listener now and the rest on
    // their turn.
    private WallPropagationStats Spend(in Frame frame, ReadOnlySpan<SoundQuery> emitters, int due)
    {
        int left = _settings.TracesPerFrame;
        int newInFull = NewSoundsInFull(left);
        int refreshed = 0;
        int unanswered = 0;

        for (int k = 0; k < due; k++)
        {
            int sound = _due[k];
            ref WallAnswer answer = ref _answers[_slots[sound]];

            // The new sounds come first in the list, loudest first.
            bool isNew = answer.Lines == 0;
            int lines = isNew && k >= newInFull ? 1 : _lines.Count;

            if (lines > left)
            {
                if (isNew)
                    unanswered++;

                continue;
            }

            Trace(in frame, in emitters[sound], ref answer, lines);
            left -= lines;
            refreshed++;
        }

        return new WallPropagationStats(
            _answers.Count, _settings.TracesPerFrame - left, refreshed, due - refreshed, unanswered);
    }

    // How many of the frame's new sounds can have every line while each of
    // the others still gets one.
    private int NewSoundsInFull(int traces)
    {
        int more = _lines.Count - 1;
        if (more == 0)
            return _newSounds;

        return Math.Clamp((traces - _newSounds) / more, 0, _newSounds);
    }

    private void Trace(in Frame frame, in SoundQuery sound, ref WallAnswer answer, int lines)
    {
        HeadLines.Ends ends = _lines.From(sound.Position, frame.Listener);
        float gain = 0f;
        float high = 0f;

        for (int line = 0; line < lines; line++)
        {
            Vector3 end = ends[line];

            // A list that ran out of room still counts for what it holds.
            int count = _world.Trace(sound.Position, end, sound.Body, _spans, out _);

            AcousticGains through = WallLoss
                .Sum(_spans.AsSpan(0, count), Vector3.Distance(sound.Position, end), _materials)
                .ToGains();

            gain += through.Gain;
            high += through.Gain * through.GainHf;
        }

        // The high end is what the lines leave at 5 kHz over what they leave
        // in all, so the open lines carry it, as an opening does.
        answer.Gain = gain / lines;
        answer.GainHf = MathF.Min(high / gain, 1f);
        answer.Lines = lines;
        answer.Sound = sound.Position;
        answer.Listener = frame.Listener;
        answer.Revision = frame.Revision;
        answer.TracedAt = frame.Now;
    }

    private void Apply(Span<SoundPaths> results)
    {
        for (int i = 0; i < results.Length; i++)
        {
            if (_slots[i] < 0)
                continue;

            ref readonly WallAnswer answer = ref _answers[_slots[i]];

            // Silent for the frame or two until its turn. Heard clear first,
            // a sound behind a wall would start loud and then go dull.
            if (answer.Lines == 0)
            {
                results[i] = SoundPaths.None;
                continue;
            }

            SoundPath path = results[i][0];
            results[i] = new SoundPaths(
                path with { Gain = path.Gain * answer.Gain, GainHf = path.GainHf * answer.GainHf });
        }
    }

    private bool HasJumped(Vector3 listener)
    {
        bool jumped = _hasListener && IsFurther(_listener, listener, JumpDistance);

        _listener = listener;
        _hasListener = true;
        return jumped;
    }

    private static bool IsFurther(Vector3 from, Vector3 to, float distance) =>
        Vector3.DistanceSquared(from, to) > distance * distance;

    private void Reserve(int sounds)
    {
        if (sounds <= _slots.Length)
            return;

        int size = Math.Max(sounds, Math.Max(16, _slots.Length * 2));
        _slots = new int[size];
        _due = new int[size];
        _urgency = new double[size];
    }

    // What one call is traced for.
    private readonly record struct Frame(Vector3 Listener, long Revision, long Now);
}
