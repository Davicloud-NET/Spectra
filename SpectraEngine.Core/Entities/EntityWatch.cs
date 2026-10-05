using SpectraEngine.Core.ConsoleSystem;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Prints what an entity world does as console lines, one event a line, for
/// the entities a mapper asked to watch. An input fired by hand is reported
/// whether or not anything is watched. Render thread only.
/// </summary>
public sealed class EntityWatch : IEntityTrace
{
    /// <summary>
    /// How many lines one tick may print. What is left over is counted and
    /// reported in one line.
    /// </summary>
    public const int MaxLinesPerTick = 64;

    // What a line names as the sender when no entity's output sent the input.
    private const string ConsoleSender = "console";
    private const string GameSender = "game";

    private readonly IConsoleOutput _output;
    private readonly List<string> _patterns = [];
    private readonly StringBuilder _line = new();

    // The world time the last input fired by hand comes due at. Negative
    // infinity when none is on its way.
    private float _handFiredDue = float.NegativeInfinity;

    private int _linesThisTick;
    private int _droppedThisTick;
    private bool _wasNeeded;

    /// <param name="output">Where the lines go.</param>
    public EntityWatch(IConsoleOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>Whether watched events are printed.</summary>
    public bool IsOn { get; private set; }

    /// <summary>
    /// What is watched: names, classes and prefixes ending in <c>*</c>. Empty
    /// means every entity.
    /// </summary>
    public IReadOnlyList<string> Patterns => _patterns;

    /// <summary>
    /// This watch while it has something to report, and null otherwise, so a
    /// world nobody watches is not traced. Assign it to the world's trace.
    /// </summary>
    public IEntityTrace? ActiveTrace => IsNeeded ? this : null;

    /// <summary>
    /// Raised when <see cref="ActiveTrace"/> has changed. A change found while
    /// a level starts is raised at the end of its first tick.
    /// </summary>
    public event Action? Changed;

    private bool IsNeeded => IsOn || _handFiredDue > float.NegativeInfinity;

    /// <summary>
    /// Turns the watch on for the entities <paramref name="patterns"/> name,
    /// or for every entity when there are none. Replaces what was watched.
    /// </summary>
    public void TurnOn(params ReadOnlySpan<string> patterns)
    {
        foreach (string pattern in patterns)
            ArgumentException.ThrowIfNullOrEmpty(pattern);

        _patterns.Clear();
        foreach (string pattern in patterns)
            _patterns.Add(pattern);

        IsOn = true;
        RaiseIfChanged();
    }

    /// <summary>Turns the watch off and forgets what was watched.</summary>
    public void TurnOff()
    {
        _patterns.Clear();
        IsOn = false;
        RaiseIfChanged();
    }

    /// <summary>
    /// Says an input is about to be queued by name on <paramref name="world"/>,
    /// <paramref name="delay"/> seconds ahead. What becomes of it is printed
    /// even with the watch off.
    /// </summary>
    public void ExpectHandFired(EntityWorld world, float delay)
    {
        ArgumentNullException.ThrowIfNull(world);

        // The sum the world makes when it queues the input, so the mark and
        // the input come due in the same tick.
        float due = world.Time + (delay > 0f ? delay : 0f);
        if (due > _handFiredDue)
            _handFiredDue = due;

        RaiseIfChanged();
    }

    /// <inheritdoc/>
    public void Record(in EntityTraceEvent traced)
    {
        bool queued = traced.Kind == EntityTraceKind.InputQueued;

        // A wire with no delay is sent in the same tick, and that line says it.
        if (queued && traced.DueTime <= traced.Time)
            return;

        if (IsFiredByHand(traced))
        {
            // The reply to the command already said it waits.
            if (queued)
                return;
        }
        else if (!IsOn || !IsWatched(traced))
        {
            return;
        }

        if (_linesThisTick >= MaxLinesPerTick)
        {
            _droppedThisTick++;
            return;
        }

        _linesThisTick++;
        Print(traced);
    }

    /// <inheritdoc/>
    public void EndTick(long tick, float time)
    {
        if (_droppedThisTick > 0)
        {
            _output.Warn(string.Create(
                CultureInfo.InvariantCulture,
                $"ent {tick} drop {EntityConsoleText.Count(_droppedThisTick, "more event", "more events")} " +
                $"this tick (limit {MaxLinesPerTick} a tick)"));
        }

        _linesThisTick = 0;
        _droppedThisTick = 0;

        // Tick zero is a new level. An input fired by hand at the one before
        // it will never arrive.
        if (tick == 0 || time >= _handFiredDue)
            _handFiredDue = float.NegativeInfinity;

        // Not at tick zero: the level is still starting, and a host that has
        // not taken the new world yet would lose the change.
        if (tick > 0)
            RaiseIfChanged();
    }

    // Queued by name with no output behind it: that is how the console fires.
    private static bool IsFiredByHand(in EntityTraceEvent traced) =>
        traced.Source is null && traced.TargetName.Length > 0;

    private bool IsWatched(in EntityTraceEvent traced)
    {
        if (_patterns.Count == 0)
            return true;

        for (int i = 0; i < _patterns.Count; i++)
        {
            string pattern = _patterns[i];
            if (traced.Source is { } source && Names(pattern, source))
                return true;
            if (traced.Target is { } target && Names(pattern, target))
                return true;

            // The name as the wire spells it, so a wire to a name nothing has
            // still shows under a pattern for that name.
            if (TargetNamePattern.Matches(pattern, traced.TargetName))
                return true;
        }

        return false;
    }

    private static bool Names(string pattern, Entity entity) =>
        EntityConsoleText.MatchesNameOrClass(pattern, entity.TargetName, entity.ClassName);

    private void Print(in EntityTraceEvent traced)
    {
        StringBuilder line = _line.Clear();
        line.Append(CultureInfo.InvariantCulture, $"ent {traced.Tick} ");

        switch (traced.Kind)
        {
            case EntityTraceKind.OutputFired:
                AppendFired(line, traced);
                _output.Print(line.ToString());
                break;

            case EntityTraceKind.InputDelivered:
                AppendRoute(line.Append("send "), traced, withParameter: true);
                AppendActivator(line, traced);
                _output.Print(line.ToString());
                break;

            case EntityTraceKind.InputQueued:
                AppendRoute(line.Append("wait "), traced, withParameter: true);
                line.Append(" in=").Append(EntityConsoleText.Seconds(traced.DueTime - traced.Time)).Append('s');
                _output.Print(line.ToString());
                break;

            case EntityTraceKind.TargetMissing:
                AppendRoute(line.Append("miss "), traced, withParameter: false);
                line.Append("  ").Append(WhyMissing(traced));
                _output.Warn(line.ToString());
                break;

            case EntityTraceKind.InputRefused when traced.Target is { } refuser:
                AppendRefused(line, refuser, traced.Input);
                _output.Warn(line.ToString());
                break;
        }
    }

    private static void AppendFired(StringBuilder line, in EntityTraceEvent traced)
    {
        line.Append("fire ");
        AppendSender(line, traced);
        line.Append(CultureInfo.InvariantCulture, $" wires={traced.WiresQueued}");
        if (traced.WiresSpent > 0)
            line.Append(CultureInfo.InvariantCulture, $" spent={traced.WiresSpent}");

        AppendActivator(line, traced);
    }

    private static void AppendRoute(StringBuilder line, in EntityTraceEvent traced, bool withParameter)
    {
        AppendSender(line, traced);
        line.Append(" -> ");
        EntityConsoleText.AppendName(line, traced.Target?.TargetName ?? traced.TargetName).Append('.');
        EntityConsoleText.AppendCall(line, traced.Input, withParameter ? traced.Parameter : "");
    }

    private static void AppendSender(StringBuilder line, in EntityTraceEvent traced)
    {
        if (traced.Source is not { } source)
        {
            line.Append(traced.TargetName.Length > 0 ? ConsoleSender : GameSender);
            return;
        }

        EntityConsoleText.AppendName(line, source.TargetName).Append('.');
        EntityConsoleText.AppendName(line, traced.Output);
    }

    // Left out when the sender started the chain itself.
    private static void AppendActivator(StringBuilder line, in EntityTraceEvent traced)
    {
        if (traced.Activator is not { } activator || ReferenceEquals(activator, traced.Source))
            return;

        EntityConsoleText.AppendName(line.Append(" by="), activator.TargetName);
    }

    private static void AppendRefused(StringBuilder line, Entity target, string input)
    {
        line.Append("deny ");
        EntityConsoleText.AppendName(line, target.TargetName).Append('.');
        EntityConsoleText.AppendName(line, input);
        line.Append("  ").Append(target.ClassName);

        if (target is PlaceholderEntity)
            line.Append(" is not in this build");
        else
            EntityConsoleText.AppendName(line.Append(" has no input "), input);
    }

    private static string WhyMissing(in EntityTraceEvent traced) =>
        traced.Target is { } gone
            ? $"{EntityConsoleText.Name(gone.TargetName)} is gone"
            : EntityConsoleText.NothingAnswers(traced.TargetName);

    private void RaiseIfChanged()
    {
        bool needed = IsNeeded;
        if (needed == _wasNeeded)
            return;

        _wasNeeded = needed;
        Changed?.Invoke();
    }
}
