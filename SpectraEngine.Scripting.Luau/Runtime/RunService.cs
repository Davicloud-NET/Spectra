namespace SpectraEngine.Scripting.Luau.Runtime;

// RunService: the signals of the fixed tick. A level runs none, one or
// several ticks in a frame, and each fires all four.
internal sealed class RunService : ScriptObject
{
    public const string ServiceName = "RunService";

    private readonly LuauHost _host;

    public RunService(LuauHost host, SignalMethods methods)
    {
        _host = host;
        PreSimulation = new ScriptSignal("Signal PreSimulation", methods);
        Stepped = new ScriptSignal("Signal Stepped", methods);
        PostSimulation = new ScriptSignal("Signal PostSimulation", methods);
        Heartbeat = new ScriptSignal("Signal Heartbeat", methods);
    }

    public override string ClassName => ServiceName;

    public override string Name => ServiceName;

    // Before the physics step, with the step in seconds.
    public ScriptSignal PreSimulation { get; }

    // Right after PreSimulation, with the level's time and the step.
    public ScriptSignal Stepped { get; }

    // At the end of the tick, with the step in seconds.
    public ScriptSignal PostSimulation { get; }

    // Right after PostSimulation, with the step in seconds.
    public ScriptSignal Heartbeat { get; }

    public override int ReadProperty(LuauStack stack, ReadOnlySpan<byte> name)
    {
        ScriptSignal? signal =
            name.SequenceEqual("Heartbeat"u8) ? Heartbeat :
            name.SequenceEqual("Stepped"u8) ? Stepped :
            name.SequenceEqual("PreSimulation"u8) ? PreSimulation :
            name.SequenceEqual("PostSimulation"u8) ? PostSimulation :
            null;

        return signal is null
            ? NoSuchProperty
            : ScriptResult.Of(_host.Objects.Push(stack, ScriptTags.Object, signal));
    }

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
