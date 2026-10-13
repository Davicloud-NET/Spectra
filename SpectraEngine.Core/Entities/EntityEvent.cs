namespace SpectraEngine.Core.Entities;

// Thinks, inputs and what scripts wait for share one record and one order.
// What scripts made waits in a queue of its own, and a drain takes whichever
// of the two queues has the earlier event next.
internal struct EntityEvent
{
    // The tick it is due on. A wait in seconds became ticks when it was queued.
    public long Tick;

    // Tiebreak: events due on one tick dispatch in the order they were made.
    // The drain also reads it to tell what was waiting from what it made.
    public long Sequence;

    public EntityEventKind Kind;

    // The thinking entity, or the entity whose output fired. Null for an
    // input nothing in the world sent.
    public Entity? Entity;

    // The one instance an input is for. Null delivers by TargetName.
    public Entity? Target;

    // A think whose serial no longer matches the entity's was superseded and
    // is dropped.
    public int ThinkSerial;

    public Entity? Activator;
    public string TargetName;
    public string Input;
    public string Parameter;

    // Only for the budget message.
    public string Output;

    // The wire's index in its sender's connection list, plus one. Zero is a
    // think or an input no wire sent, so an event built without it is not
    // taken for wire zero.
    public int WireOrdinal;

    // What the script runtime asked to be handed back: the thread of a wake,
    // or the listener of a fired output.
    public long Token;

    // The file and the line of the script that sent an input. Null and zero
    // for what no script sent. Only a trace reads them.
    public string? SenderScript;
    public int SenderLine;

    // Who touches, for a touch on its way to a listener: a TouchVisitor's id.
    public int Visitor;
}
