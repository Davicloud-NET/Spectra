using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>The signals of the fixed tick. A level runs none, one or several ticks in a frame, and each fires all four.</summary>
[ScriptClass("RunService")]
internal sealed partial class RunService : ScriptObject
{
    public RunService(SignalHub hub)
    {
        PreSimulation = new ScriptSignal("Signal PreSimulation", hub);
        Stepped = new ScriptSignal("Signal Stepped", hub);
        PostSimulation = new ScriptSignal("Signal PostSimulation", hub);
        Heartbeat = new ScriptSignal("Signal Heartbeat", hub);
    }

    public override string Name => "RunService";

    /// <summary>Fires in every tick before the physics step, with the step in seconds.</summary>
    [ScriptSignal("deltaTime: number")]
    public ScriptSignal PreSimulation { get; }

    /// <summary>Fires right after PreSimulation, with the level's time and the step, both in seconds.</summary>
    [ScriptSignal("time: number, deltaTime: number")]
    public ScriptSignal Stepped { get; }

    /// <summary>Fires at the end of every tick, with the step in seconds.</summary>
    [ScriptSignal("deltaTime: number")]
    public ScriptSignal PostSimulation { get; }

    /// <summary>Fires right after PostSimulation, with the step in seconds.</summary>
    [ScriptSignal("deltaTime: number")]
    public ScriptSignal Heartbeat { get; }

    public void FireBeforeSimulation(double time, double step)
    {
        if (PreSimulation.ListenerCount > 0)
            PreSimulation.Fire([step]);
        if (Stepped.ListenerCount > 0)
            Stepped.Fire([time, step]);
    }

    public void FireAfterSimulation(double step)
    {
        if (PostSimulation.ListenerCount > 0)
            PostSimulation.Fire([step]);
        if (Heartbeat.ListenerCount > 0)
            Heartbeat.Fire([step]);
    }
}
