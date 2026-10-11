namespace SpectraEngine.Core.Scripting;

/// <summary>
/// Takes what scripts report. Called on the thread that runs them, in the
/// middle of a tick, so it should not throw. What it throws while a level
/// plays is dropped, and the message with it.
/// </summary>
public interface IScriptSink
{
    /// <summary>Takes one message.</summary>
    void Report(in ScriptMessage message);
}
