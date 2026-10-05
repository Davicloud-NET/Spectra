using SpectraEngine.Core.Entities;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

// Keeps every output that fired, wired or not, as "tick:Entity.Output".
internal sealed class FiredOutputLog : IEntityTrace
{
    public List<string> Fired { get; } = [];

    public void Record(in EntityTraceEvent traced)
    {
        if (traced.Kind == EntityTraceKind.OutputFired && traced.Source is { } source)
            Fired.Add($"{traced.Tick}:{source.TargetName}.{traced.Output}");
    }

    public void EndTick(long tick, float time)
    {
    }
}
