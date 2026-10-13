using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The player's mover as the scripts of one level set it: the four objects
// every mover is handed, the mover that moves the player now, and whether
// one has failed. PlayerWrites puts the mover in and records the one the
// level started with, so Stop puts that one back.
internal sealed class MoverSlot
{
    // What SetMover takes, as Luau writes it.
    public const string Signature =
        "mover: ((state: CharacterState, command: CharacterCommand, collision: CharacterCollision, " +
        "tuning: CharacterTuning, deltaTime: number) -> ())?";

    public const string FailedBefore =
        "A mover failed in this level, so the built-in mover moves the player until the level is played again.";

    private readonly PlayerWrites _door;
    private Handed? _handed;
    private LuauCharacterMover? _mover;
    private bool _hasFailed;

    public MoverSlot(PlayerWrites door) => _door = door;

    // character:SetMover(mover). The function is the second value, or nil
    // for the built-in mover.
    public int Set(ScriptContext context, LuauStack stack)
    {
        if (stack.IsNil(2))
            return Install(stack, null);
        if (stack.TypeOf(2) != LuauType.Function)
            return ScriptArguments.Wrong(stack, 2, "SetMover", "function", isMethod: true);

        // Not again in this level: a mover that fails on its first tick
        // and is installed on every tick would fill the log.
        if (_hasFailed)
            return stack.Fail(FailedBefore);
        if ((_handed ??= MakeHanded(context, stack)) is not { } handed)
            return stack.Fail("the mover was not installed: Luau could not keep what a mover is handed");

        LuauHost host = context.Host;
        if (host.KeepFunction(stack, 2) is not { } function)
            return stack.Fail("the mover was not installed: Luau could not keep the function");
        if (MoverCall.Make(host, function) is not { } call)
        {
            function.Dispose();
            return stack.Fail("the mover was not installed: Luau made no thread for it");
        }

        if (_mover is { Script: { } owner } && !ReferenceEquals(owner, function.Script))
        {
            host.Report(ScriptReport.Line(
                stack.State, function.Script, ScriptSeverity.Warning,
                $"This mover takes the place of the one {owner.Name} installed."));
        }

        return Install(stack, new LuauCharacterMover(host, call, handed.Frame, handed.Collision, handed.Values, Failed));
    }

    // The built-in mover goes into the mover's place, so nothing asks the
    // one that failed again. That one finishes the tick it failed in.
    private void Failed()
    {
        _hasFailed = true;
        _mover = null;
        _door.TrySetMover(null, out _);
    }

    private int Install(LuauStack stack, LuauCharacterMover? mover)
    {
        if (!_door.TrySetMover(mover, out string? refused))
        {
            mover?.LetGo();
            return stack.Fail(refused);
        }

        _mover?.LetGo();
        _mover = mover;
        return 0;
    }

    // Made once and kept for the level, so every tick hands a mover the
    // same four objects and Luau makes none. Null when Luau kept none.
    private Handed? MakeHanded(ScriptContext context, LuauStack stack)
    {
        var frame = new MoverFrame(_door, context.World);
        var collision = new MoverCollision(frame);
        ScriptObject[] objects = [new MoverState(frame), new MoverCommand(frame), collision, new MoverTuning(frame)];
        var values = new LuauReference[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            LuauReference? kept = context.PushObject(stack, objects[i]) ? context.Host.Keep(stack, -1) : null;
            stack.Pop();
            if (kept is null)
            {
                for (int made = 0; made < i; made++)
                    values[made].Dispose();

                return null;
            }

            values[i] = kept;
        }

        return new Handed(frame, collision, values);
    }

    private sealed record Handed(MoverFrame Frame, MoverCollision Collision, LuauReference[] Values);
}
