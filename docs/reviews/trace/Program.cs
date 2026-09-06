using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

string path = TraceLog.CreateFromEventPipeDataFile(args[0]);
using var log = new TraceLog(path);
var allocations = new Dictionary<string, (long Bytes, int Samples)>();
foreach (var ev in log.Events)
{
    if (ev is not GCAllocationTickTraceData allocation) continue;
    var stack = ev.CallStack();
    var frames = new List<string>();
    while (stack is not null)
    {
        string name = stack.CodeAddress.FullMethodName;
        if (name.Contains("Spectra")) frames.Add(name);
        stack = stack.Caller;
    }
    if (!frames.Any(f => f.Contains("CsgIncrementalCompiler"))) continue;
    string key = allocation.TypeName + " | " + string.Join(" <- ", frames.Take(4));
    var prev = allocations.GetValueOrDefault(key);
    allocations[key] = (prev.Bytes + allocation.AllocationAmount64, prev.Samples + 1);
}
Console.WriteLine("Sampled allocation amounts with CsgIncrementalCompiler on stack (not exact object totals).");
foreach (var (key, total) in allocations.OrderByDescending(x => x.Value.Bytes).Take(30))
    Console.WriteLine($"{total.Bytes / 1048576d:F3} MiB | {total.Samples} samples | {key}");
