namespace SpectraEngine.Core.Scripting;

// For a host that set no sink: what its scripts report goes nowhere.
internal sealed class SilentScriptSink : IScriptSink
{
    public static readonly SilentScriptSink Instance = new();

    public void Report(in ScriptMessage message)
    {
    }
}
