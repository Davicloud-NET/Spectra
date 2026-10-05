using System;
using System.Numerics;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Diagnostics;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// Propagation through walls: a sound is as loud as its distance says, times
/// what the solids between it and the listener let through. Render thread only.
/// </summary>
// It keeps an answer for each sound and traces the ones that are due, within
// a number of lines a frame.
public sealed class WallPropagation : ISoundPropagation
{
    /// <summary>A sound at or below this gain from distance alone is not traced. 60 dB down.</summary>
    public const float InaudibleGain = SoundPresenter.SilenceGain;

    /// <summary>
    /// A listener or a sound that is further than this from where it last
    /// was has jumped. Its old answer is dropped, not kept until its turn.
    /// </summary>
    public const float JumpDistance = SoundPresenter.ListenerJumpDistance;

    // What a sound's turn is ranked by, most urgent first: one with no answer,
    // one traced along a single line so far, one whose answer has grown old,
    // and one whose world, listener or place changed. Within the first, second
    // and last the louder goes first, within the third the older.
    private const double NewSound = 3000;
    private const double OneLine = 2000;
    private const double GrownOld = 1000;
    private const double OldestCounted = 900;

    private readonly ISoundObstacles _world;
    private readonly WallPropagationSettings _settings;
    private readonly TimeProvider _clock;
    private readonly double _secondsPerTick;

    private readonly DirectPropagation _distance = new();
    private readonly SoundLines _lines;
    private readonly WallAnswers _answers = new();

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
        _clock = clock ?? TimeProvider.System;
        _secondsPerTick = 1d / _clock.TimestampFrequency;
        _lines = new SoundLines(world, materials, _settings);
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
            Forget();
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

    /// <inheritdoc/>
    // A sound is known by its node's id, which the next play session has too.
    public void Forget()
    {
        _answers.Clear();
        _hasListener = false;
        Stats = default;
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

        bool changed = IsFurther(answer.Listener, frame.Listener, _settings.MoveDistance)
            || IsFurther(answer.Sound, sound, _settings.MoveDistance)
            || HasWorldChanged(ref answer, frame.Revision);

        return changed ? gain : 0;
    }

    // A compile that landed away from an answer's lines leaves it standing.
    // A world that animates compiles every frame.
    private bool HasWorldChanged(ref WallAnswer answer, long revision)
    {
        if (answer.Revision == revision)
            return false;

        if (_world.HasChangedSince(answer.Revision, answer.Sound, answer.Listener, _settings.HeadRadius))
            return true;

        answer.Revision = revision;
        return false;
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
        AcousticGains through = _lines.Through(in sound, frame.Listener, lines);

        answer.Gain = through.Gain;
        answer.GainHf = through.GainHf;
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
