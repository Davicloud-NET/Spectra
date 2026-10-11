using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Lets scripts print so many lines in one tick and drops the rest, so a loop
// of prints cannot fill the console and push an error out. Warnings and
// errors always pass. A tick that dropped lines says how many.
internal sealed class PrintCap(IScriptSink sink, EntityWorld world) : IScriptSink
{
    // A frame runs up to five ticks, and the console holds 1024 lines a frame.
    public const int MaxPrintsPerTick = 100;

    private long _tick = -1;
    private int _printed;
    private int _dropped;
    private string _lastDroppedScript = string.Empty;
    private int _lastDroppedLine;

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

        if (_printed < MaxPrintsPerTick)
        {
            _printed++;
            sink.Report(in message);
            return;
        }

        _dropped++;
        _lastDroppedScript = message.Script;
        _lastDroppedLine = message.Line;
    }

    // Says what the tick dropped, at the place of the last dropped line.
    public void EndTick()
    {
        int dropped = _dropped;
        _printed = 0;
        _dropped = 0;
        if (dropped == 0)
            return;

        sink.Report(new ScriptMessage(
            ScriptSeverity.Warning, Describe(dropped), _lastDroppedScript, _lastDroppedLine));
    }

    internal static string Describe(int dropped)
    {
        string lost = dropped == 1
            ? "1 more line was printed in this tick and dropped."
            : $"{dropped} more lines were printed in this tick and dropped.";

        return $"{lost} Scripts may print {MaxPrintsPerTick} lines a tick.";
    }
}
