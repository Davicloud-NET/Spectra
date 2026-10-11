using System.Runtime.InteropServices;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Lets scripts print so many lines in one tick and drops the rest, so a loop
// of prints cannot fill the console and push an error out. Warnings and
// errors always pass. A tick that dropped lines says how many, at the script
// that printed the most.
internal sealed class PrintCap(IScriptSink sink, EntityWorld world) : IScriptSink
{
    // Sized for the console, which keeps 1024 lines between two snapshots. At
    // the default EngineHost.SnapshotInterval that is five or six ticks. A host
    // that publishes less often loses prints there, and still gets the errors.
    public const int MaxPrintsPerTick = 100;

    // How many lines each script printed in this tick, the dropped ones too.
    private readonly Dictionary<string, int> _printedBy = new(StringComparer.Ordinal);

    private long _tick = -1;
    private int _printed;
    private int _dropped;
    private string _busiestScript = string.Empty;
    private int _busiestCount;
    private int _busiestLine;

    public void Report(in ScriptMessage message)
    {
        // A host that never ends a tick still starts the next one.
        if (world.TickNumber != _tick)
        {
            EndTick();
            _tick = world.TickNumber;
        }

        if (message.Severity != ScriptSeverity.Print)
        {
            sink.Report(in message);
            return;
        }

        Count(in message);
        if (_printed < MaxPrintsPerTick)
        {
            _printed++;
            sink.Report(in message);
            return;
        }

        _dropped++;
    }

    // Says what the tick dropped, where the script that printed the most
    // printed last. The line that was dropped last can be the one line of a
    // script that came after the flood.
    public void EndTick()
    {
        int dropped = _dropped;
        string script = _busiestScript;
        int line = _busiestLine;

        _printed = 0;
        _dropped = 0;
        _busiestScript = string.Empty;
        _busiestCount = 0;
        _busiestLine = 0;
        _printedBy.Clear();

        if (dropped > 0)
            sink.Report(new ScriptMessage(ScriptSeverity.Warning, Describe(dropped), script, line));
    }

    internal static string Describe(int dropped)
    {
        string lost = dropped == 1
            ? "1 more line was printed in this tick and dropped."
            : $"{dropped} more lines were printed in this tick and dropped.";

        return $"{lost} Scripts may print {MaxPrintsPerTick} lines a tick.";
    }

    // The first script to reach a count keeps the lead, so two that print as
    // much as each other do not swap places.
    private void Count(in ScriptMessage message)
    {
        ref int count = ref CollectionsMarshal.GetValueRefOrAddDefault(_printedBy, message.Script, out _);
        count++;
        if (count <= _busiestCount)
            return;

        _busiestScript = message.Script;
        _busiestCount = count;
        _busiestLine = message.Line;
    }
}
