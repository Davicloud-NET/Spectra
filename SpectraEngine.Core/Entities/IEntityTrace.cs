namespace SpectraEngine.Core.Entities;

/// <summary>
/// Is told what an <see cref="EntityWorld"/> fires, queues and delivers, as it
/// happens. Called on the render thread from inside the world's dispatch, so
/// an implementation must not change the world: no firing, queueing, spawning
/// or despawning.
/// </summary>
public interface IEntityTrace
{
    /// <summary>
    /// Reports one event. Copy out the names and numbers before returning. An
    /// entity kept past the call goes stale when the world stops.
    /// </summary>
    void Record(in EntityTraceEvent traced);

    /// <summary>
    /// Reports that a tick's work is done. Called with tick zero once the
    /// level has spawned.
    /// </summary>
    void EndTick(long tick, float time);
}
