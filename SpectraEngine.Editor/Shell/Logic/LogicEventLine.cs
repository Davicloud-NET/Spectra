using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>One thing a wire did while the level ran, as a line of text.</summary>
/// <param name="Number">The event's number, which tells it from every other.</param>
/// <param name="Tick">The tick it happened on.</param>
/// <param name="Route">Who sent what to whom: the sender and its output, an arrow, the receiver and its input.</param>
/// <param name="Reason">Why it did not arrive, or empty when it did.</param>
public sealed record LogicEventLine(long Number, string Tick, string Route, string Reason)
{
    private const string Arrow = "→";

    /// <summary>Whether the input did not arrive.</summary>
    public bool IsFailure => Reason.Length > 0;

    /// <summary>Writes an event as a line.</summary>
    public static LogicEventLine From(in LogicEventInfo info)
    {
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

        return new LogicEventLine(info.Number, info.Tick.ToString(CultureInfo.InvariantCulture), route, reason);
    }
}
