using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// An entity that reports the parameter of every Say it gets where scripts
// print, so a test reads wires and scripts in one order. It then fires
// OnSaid, for a level that wires one to the next.
internal sealed class SayingEntity(IScriptSink sink) : Entity
{
    public const string Class = "sayer";

    public const string Said = "OnSaid";

    public override bool AcceptInput(string input, ref EntityInputContext context)
    {
        if (!string.Equals(input, "Say", StringComparison.Ordinal))
            return false;

        sink.Report(new ScriptMessage(ScriptSeverity.Print, context.Parameter, string.Empty, 0));
        FireOutput(Said);
        return true;
    }
}
