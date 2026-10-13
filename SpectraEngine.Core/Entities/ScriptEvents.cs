namespace SpectraEngine.Core.Entities;

// The two events a script puts on the queue beside the wake of a thread: an
// input it sends, and a fired output on its way to one listener.
internal static class ScriptEvents
{
    // For one entity and due at once, as door:Open() is.
    public static EntityEvent Input(
        long tick, long sequence, Entity target, string input, string parameter, string script, int line) => new()
        {
            Tick = tick,
            Sequence = sequence,
            Kind = EntityEventKind.Input,
            Target = target,
            TargetName = "",
            Input = input,
            Parameter = parameter,
            Output = "",
            SenderScript = script,
            SenderLine = line,
        };

    public static EntityEvent Signal(
        long tick, long sequence, Entity source, string output, string parameter, long listener) => new()
        {
            Tick = tick,
            Sequence = sequence,
            Kind = EntityEventKind.ScriptSignal,
            Entity = source,
            Token = listener,
            TargetName = "",
            Input = "",
            Parameter = parameter,
            Output = output,
        };

    // No entity fires a touch, so the part is here by its name, which only a
    // message reads.
    public static EntityEvent Touch(
        long tick, long sequence, string part, string signal, int visitor, long listener) => new()
        {
            Tick = tick,
            Sequence = sequence,
            Kind = EntityEventKind.ScriptTouch,
            Token = listener,
            Visitor = visitor,
            TargetName = part,
            Input = "",
            Parameter = "",
            Output = signal,
        };
}
