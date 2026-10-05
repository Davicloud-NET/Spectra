using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

// Writes each event out as names and numbers inside Record, so it keeps no
// entity. A line reads "send source.Output -> target.Input(parameter) by=activator",
// with "-" where no output sent the input.
internal sealed class RecordingEntityTrace : IEntityTrace
{
    public List<Entry> Entries { get; } = [];

    public List<long> EndedTicks { get; } = [];

    public IEnumerable<string> Lines => Entries.Select(entry => entry.Line);

    public void Record(in EntityTraceEvent traced) =>
        Entries.Add(new Entry(
            traced.Kind,
            traced.Tick,
            traced.Time,
            traced.DueTime,
            Describe(traced),
            traced.WiresQueued,
            traced.WiresSpent));

    public void EndTick(long tick, float time) => EndedTicks.Add(tick);

    private static string Describe(in EntityTraceEvent traced)
    {
        string by = traced.Activator is { } activator ? $" by={activator.TargetName}" : "";

        if (traced.Kind == EntityTraceKind.OutputFired)
            return $"fire {traced.Source!.TargetName}.{traced.Output}{by}";

        string verb = traced.Kind switch
        {
            EntityTraceKind.InputQueued => "queue",
            EntityTraceKind.InputDelivered => "send",
            EntityTraceKind.InputRefused => "deny",
            _ => "miss",
        };
        string from = traced.Source is { } source ? $"{source.TargetName}.{traced.Output}" : "-";
        string to = traced.Target?.TargetName ?? traced.TargetName;
        string parameter = traced.Parameter.Length > 0 ? $"({traced.Parameter})" : "";

        return $"{verb} {from} -> {to}.{traced.Input}{parameter}{by}";
    }

    internal sealed record Entry(
        EntityTraceKind Kind,
        long Tick,
        float Time,
        float DueTime,
        string Line,
        int WiresQueued,
        int WiresSpent);
}
