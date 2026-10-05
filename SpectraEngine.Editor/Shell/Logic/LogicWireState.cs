using SpectraEngine.Core.Inspection;
using System;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>What a wire looks like right now, and what its label says about it.</summary>
public readonly record struct LogicWireState
{
    /// <summary>How many ticks after it fired a wire still counts as firing.</summary>
    public const int FiringTicks = 20;

    /// <summary>The fewest digits of a count a label keeps room for.</summary>
    public const int LeastDigits = 3;

    /// <summary>How the wire is drawn.</summary>
    public LogicWireLook Look { get; init; }

    /// <summary>
    /// The number <see cref="Text"/> is written from: a count, or the tenths
    /// of a second left to wait.
    /// </summary>
    public int Amount { get; init; }

    /// <summary>Whether a broken wire was refused. False when its target matched nothing.</summary>
    public bool IsRefusal { get; init; }

    /// <summary>
    /// How far along the wire a waiting input has come, from 0 to 1. Null
    /// when nothing waits.
    /// </summary>
    public double? Travel { get; init; }

    /// <summary>Whether the wire has something to say that is not its authored label.</summary>
    public bool HasText => Look switch
    {
        LogicWireLook.Broken => Amount > 0,
        LogicWireLook.Waiting or LogicWireLook.Firing or LogicWireLook.Fired => true,
        _ => false,
    };

    /// <summary>What the label says, or empty when the authored label stands.</summary>
    public string Text => Look switch
    {
        LogicWireLook.Broken when Amount > 0 => (IsRefusal ? "refused " : "missed ") + Number(Amount),
        LogicWireLook.Waiting => $"in {Number(Amount / 10)}.{Number(Amount % 10)} s",
        LogicWireLook.Firing => "now",
        LogicWireLook.Fired => Amount == 1 ? "1 time" : Number(Amount) + " times",
        _ => "",
    };

    /// <summary>Whether two states read the same, wherever the waiting input is.</summary>
    public bool SaysTheSameAs(LogicWireState other) =>
        Look == other.Look && Amount == other.Amount && IsRefusal == other.IsRefusal;

    /// <summary>A wire nothing has happened to: as the level was authored.</summary>
    public static LogicWireState Authored(bool goesNowhere, bool touchesSelection) => new()
    {
        Look = goesNowhere ? LogicWireLook.Broken
            : touchesSelection ? LogicWireLook.Focus
            : LogicWireLook.Plain,
    };

    /// <summary>A wire of a running level.</summary>
    /// <param name="activity">What the wire has done. Several wires drawn as one are summed first.</param>
    /// <param name="tick">The level's tick.</param>
    /// <param name="time">The level's time in seconds.</param>
    /// <param name="authored">What the wire is while nothing has happened to it.</param>
    public static LogicWireState Playing(
        in LogicWireActivity activity,
        long tick,
        float time,
        LogicWireState authored)
    {
        if (activity.Missed > 0)
            return new LogicWireState { Look = LogicWireLook.Broken, Amount = activity.Missed };

        if (activity.Refused > 0)
            return new LogicWireState { Look = LogicWireLook.Broken, Amount = activity.Refused, IsRefusal = true };

        if (activity.Waiting > 0)
            return Wait(activity, time);

        if (activity.Fired <= 0)
            return authored;

        return tick - activity.LastFiredTick < FiringTicks
            ? new LogicWireState { Look = LogicWireLook.Firing }
            : new LogicWireState { Look = LogicWireLook.Fired, Amount = activity.Fired };
    }

    /// <summary>What two wires drawn as one have done together.</summary>
    public static LogicWireActivity Sum(in LogicWireActivity a, in LogicWireActivity b)
    {
        // The input that is due first is the one the dot follows.
        bool takesB = b.Waiting > 0 && (a.Waiting == 0 || b.WaitingDue < a.WaitingDue);

        return new LogicWireActivity
        {
            NodeId = a.NodeId,
            Wire = a.Wire,
            Fired = a.Fired + b.Fired,
            LastFiredTick = Math.Max(a.LastFiredTick, b.LastFiredTick),
            Missed = a.Missed + b.Missed,
            Refused = a.Refused + b.Refused,
            Waiting = a.Waiting + b.Waiting,
            WaitingSince = takesB ? b.WaitingSince : a.WaitingSince,
            WaitingDue = takesB ? b.WaitingDue : a.WaitingDue,
        };
    }

    /// <summary>
    /// The longest things a label can say about a running wire whose counts
    /// have <paramref name="digits"/> digits. A view keeps room for the widest.
    /// </summary>
    public static string[] LongestTexts(int digits)
    {
        string nines = new('9', Math.Max(digits, 1));
        return [$"refused {nines}", $"missed {nines}", $"{nines} times", $"in {nines}.9 s"];
    }

    /// <summary>How many digits the largest number in a wire's label has.</summary>
    public static int Digits(in LogicWireActivity activity, float time)
    {
        int seconds = activity.Waiting > 0 ? (int)Math.Max(0, activity.WaitingDue - time) : 0;
        int largest = Math.Max(Math.Max(activity.Fired, seconds), Math.Max(activity.Missed, activity.Refused));

        int digits = 1;
        for (int rest = largest; rest >= 10; rest /= 10)
            digits++;

        return digits;
    }

    private static LogicWireState Wait(in LogicWireActivity activity, float time)
    {
        float left = Math.Max(0f, activity.WaitingDue - time);
        float whole = activity.WaitingDue - activity.WaitingSince;

        return new LogicWireState
        {
            Look = LogicWireLook.Waiting,
            Amount = (int)MathF.Round(left * 10f, MidpointRounding.AwayFromZero),
            Travel = whole <= 0f ? 1 : Math.Clamp((time - activity.WaitingSince) / whole, 0f, 1f),
        };
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
