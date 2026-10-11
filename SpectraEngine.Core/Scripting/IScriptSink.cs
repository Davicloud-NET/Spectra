namespace SpectraEngine.Core.Scripting;

/// <summary>Takes what scripts report. Called on the thread that runs them.</summary>
public interface IScriptSink
{
    /// <summary>Takes one message.</summary>
    void Report(in ScriptMessage message);
}
