using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>One thing a wire or a script did while the level ran, as a line of text.</summary>
/// <param name="Number">The event's number, which tells it from every other.</param>
/// <param name="Tick">The tick it happened on.</param>
/// <param name="Route">Who sent what to whom: the sender and its output, an arrow, the receiver and its input.</param>
/// <param name="Reason">Why it did not arrive, or empty when it did.</param>
public sealed record LogicEventLine(long Number, string Tick, string Route, string Reason)
{
    private const string Arrow = "→";

    /// <summary>Whether the input did not arrive.</summary>
    public bool IsFailure => Reason.Length > 0;

    /// <summary>
    /// The line of code a script sent it from or connected to it on, such as
    /// "DoorController, line 42". Empty for what no script had a part in.
    /// </summary>
    public string Place { get; init; } = "";

    /// <summary>Whether <see cref="Place"/> has something to say.</summary>
    public bool HasPlace => Place.Length > 0;

    /// <summary>Writes an event as a line.</summary>
    public static LogicEventLine From(in LogicEventInfo info)
    {
        if (info.IsOfScript)
            return OfScript(info);

        string target = $"{info.TargetName}.{info.Input}";

        // An input a host sent has no output behind it.
        string route = string.IsNullOrEmpty(info.SourceName) && string.IsNullOrEmpty(info.Output)
            ? $"{Arrow} {target}"
            : $"{info.SourceName}.{info.Output} {Arrow} {target}";

        string reason = info.Kind switch
        {
            EntityTraceKind.TargetMissing => $"nothing is named {info.TargetName}",
            EntityTraceKind.InputRefused => $"{info.TargetName} has no input {info.Input}",
            _ => "",
        };

        return new LogicEventLine(info.Number, TickOf(info.Tick), route, reason);
    }

    // A script is the receiver of what it heard and the sender of what it sent.
    private static LogicEventLine OfScript(in LogicEventInfo info)
    {
        string route = info.Kind == EntityTraceKind.OutputFired
            ? $"{info.SourceName}.{info.Output} {Arrow} {info.ScriptName}"
            : $"{info.ScriptName} {Arrow} {info.TargetName}.{info.Input}";

        // A script sends to an entity it holds, so a miss is an entity that left.
        string reason = info.Kind switch
        {
            EntityTraceKind.TargetMissing => $"{info.TargetName} is gone",
            EntityTraceKind.InputRefused => $"{info.TargetName} has no input {info.Input}",
            _ => "",
        };

        return new LogicEventLine(info.Number, TickOf(info.Tick), route, reason)
        {
            Place = LogicLabel.Place(info.ScriptFile, info.ScriptLine).Text,
        };
    }

    private static string TickOf(long value) => value.ToString(CultureInfo.InvariantCulture);
}
