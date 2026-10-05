using SpectraEngine.Core.Entities;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

// Keeps every output that fired, wired or not, as "tick:Entity.Output". A
// wire that was sent off with a value is kept too, as "tick:Entity.Output:value",
// since a fired output does not say what it carries.
internal sealed class FiredOutputLog : IEntityTrace
{
    public List<string> Fired { get; } = [];

    public List<string> Carried { get; } = [];

    public void Record(in EntityTraceEvent traced)
    {
        if (traced.Source is not { } source)
            return;

        if (traced.Kind == EntityTraceKind.OutputFired)
            Fired.Add($"{traced.Tick}:{source.TargetName}.{traced.Output}");
        else if (traced.Kind == EntityTraceKind.InputQueued && traced.Parameter.Length > 0)
            Carried.Add($"{traced.Tick}:{source.TargetName}.{traced.Output}:{traced.Parameter}");
    }

    public void EndTick(long tick, float time)
    {
    }
}
