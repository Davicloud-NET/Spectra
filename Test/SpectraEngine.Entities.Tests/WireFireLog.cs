using SpectraEngine.Core.Entities;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

// Keeps the tick each wired output fired on, as "tick:Output" or
// "tick:Output:parameter". A wire is queued on the tick its output fires and
// delivered on the next, so this is the firing tick, not the one a sink hears.
internal sealed class WireFireLog : IEntityTrace
{
    public List<string> Fired { get; } = [];

    public void Record(in EntityTraceEvent traced)
    {
        if (traced.Kind != EntityTraceKind.InputQueued || traced.Output.Length == 0)
            return;

        Fired.Add(traced.Parameter.Length > 0
            ? $"{traced.Tick}:{traced.Output}:{traced.Parameter}"
            : $"{traced.Tick}:{traced.Output}");
    }

    public void EndTick(long tick, float time)
    {
    }
}
