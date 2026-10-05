namespace SpectraEngine.Core.Entities;

// A world has one trace slot. This lets two watchers share it.
internal sealed class EntityTracePair(IEntityTrace first, IEntityTrace second) : IEntityTrace
{
    public static IEntityTrace? Join(IEntityTrace? first, IEntityTrace? second)
    {
        if (first is null)
            return second;

        return second is null ? first : new EntityTracePair(first, second);
    }

    public void Record(in EntityTraceEvent traced)
    {
        first.Record(traced);
        second.Record(traced);
    }

    public void EndTick(long tick, float time)
    {
        first.EndTick(tick, time);
        second.EndTick(tick, time);
    }
}
