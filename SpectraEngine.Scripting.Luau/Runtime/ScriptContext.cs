using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using System.Text.Unicode;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What generated glue reaches a level through: the host, the method table
// of each class, and the door. ScriptWrites is the only thing here that
// sets a node, so Stop puts every write back. A member written by hand may
// take the context too, and finds here what the level keeps for it.
internal sealed class ScriptContext
{
    // In bytes of UTF-8. A longer text is made into a string on every write.
    private const int HeldTextLimit = 512;

    private readonly ScriptWrites _writes;
    private readonly MethodTable?[] _methods;

    // Gives the host the atoms and makes the method tables, so it comes
    // before anything else of the script API is set up.
    public ScriptContext(LuauHost host, ScriptWrites writes, ChildWaits childWaits)
    {
        Host = host;
        ChildWaits = childWaits;
        _writes = writes;

        if (ScriptApiCatalog.AtomCount > 0)
            host.SetAtoms(ScriptApiCatalog.AtomNames, ScriptApiCatalog.AtomCount);

        _methods = ScriptApiCatalog.BuildMethods(this);
    }

    public LuauHost Host { get; }

    // The threads that wait for a child, which WaitForChild adds to.
    public ChildWaits ChildWaits { get; }

    // The inputs and outputs a node has from its entity class. The runtime
    // sets it once the signals behind it are made. Null until then.
    public EntityMembers? Entities { get; set; }

    // The signals the level's queue fires, where a part's Touched is made.
    // The runtime sets it with Entities.
    public NodeSignals? Signals { get; set; }

    // The level, for what a script only reads of it. A change of a node
    // goes through the writes below and no other way.
    public EntityWorld World => _writes.World;

    // Nil for null. False: an error value is on top instead.
    public bool PushObject(LuauStack stack, object? value) =>
        value is null ? stack.PushNil() : Host.Objects.Push(stack, ScriptTags.Of(value), value);

    public bool TryGetObject<T>(LuauStack stack, int index, [NotNullWhen(true)] out T? value)
        where T : class
    {
        value = ScriptTags.TryGet(Host.Objects, stack, index, out object? target) == LuauObjectLookup.Found
            ? target as T
            : null;
        return value is not null;
    }

    // The function a script gets when it reads a method and does not call it.
    public int PushMethod(LuauStack stack, int classId, ReadOnlySpan<byte> name) =>
        _methods[classId] is { } methods && methods.TryPush(stack, name)
            ? 1
            : stack.Fail($"the method {Encoding.UTF8.GetString(name)} was not set up");

    public void SetGlobalFunction(ReadOnlySpan<byte> name, LuauHostFunction function)
    {
        LuauStack stack = Host.Stack;
        if (!Host.PushFunction(stack, name, function) || !stack.SetGlobal(name))
            throw NotSetUp(stack);
    }

    public void SetGlobalObject(ReadOnlySpan<byte> name, object value)
    {
        LuauStack stack = Host.Stack;
        if (!PushObject(stack, value) || !stack.SetGlobal(name))
            throw NotSetUp(stack);
    }

    // The writes below take the value from the third argument of
    // __newindex, check its kind and hand it to the door.
    public int WriteVector(LuauStack stack, SceneNode node, SettableMemberId member) =>
        stack.TryGetVector(3, out Vector3 value)
            ? Write(stack, node, member, MemberValue.Of(value))
            : WrongKind(stack, member);

    public int WriteFrame(LuauStack stack, SceneNode node, SettableMemberId member) =>
        FrameValue.TryGet(stack, 3, out CFrame value)
            ? Write(stack, node, member, MemberValue.Of(value))
            : WrongKind(stack, member);

    public int WriteText(LuauStack stack, SceneNode node, SettableMemberId member)
    {
        if (!stack.TryGetUtf8(3, out ReadOnlySpan<byte> text))
            return WrongKind(stack, member);

        // A string of Luau is bytes. Bytes that are no text would read back
        // as other bytes than the script wrote.
        if (!Utf8.IsValid(text))
            return stack.Fail($"Unable to assign property {ScriptWrites.Settable[(int)member].Name}. The string is not valid UTF-8");

        return Write(stack, node, member, MemberValue.Of(TextOf(text, ScriptWrites.Read(node, member).Text)));
    }

    public int WriteBoolean(LuauStack stack, SceneNode node, SettableMemberId member) =>
        ScriptArguments.TryBoolean(stack, 3, out bool value)
            ? Write(stack, node, member, MemberValue.Of(value))
            : WrongKind(stack, member);

    // A string that reads as a number counts as one, as it does in Luau.
    public int WriteNumber(LuauStack stack, SceneNode node, SettableMemberId member) =>
        ScriptArguments.TryDouble(stack, 3, out double value)
            ? Write(stack, node, member, MemberValue.Of(value))
            : WrongKind(stack, member);

    public int WriteColor(LuauStack stack, SceneNode node, SettableMemberId member) =>
        ColorValue.TryGet(stack, 3, out Color3 value)
            ? Write(stack, node, member, MemberValue.Of(value))
            : WrongKind(stack, member);

    private int Write(LuauStack stack, SceneNode node, SettableMemberId member, in MemberValue value) =>
        _writes.TryWrite(node, member, in value, out string? refusal) ? 0 : stack.Fail(refusal);

    private static int WrongKind(LuauStack stack, SettableMemberId member) =>
        stack.Fail(ScriptWrites.WrongKind(ScriptWrites.Settable[(int)member], ScriptArguments.TypeName(stack, 3)));

    // The text the member holds is handed back as it is. A script that
    // writes the same name on every tick then makes no string.
    private static string TextOf(ReadOnlySpan<byte> utf8, string held)
    {
        Span<byte> same = stackalloc byte[HeldTextLimit];
        bool isHeld = utf8.Length <= same.Length
            && Encoding.UTF8.TryGetBytes(held, same, out int written)
            && utf8.SequenceEqual(same[..written]);

        return isHeld ? held : Encoding.UTF8.GetString(utf8);
    }

    private static InvalidOperationException NotSetUp(LuauStack stack) =>
        new($"The script API could not be set up: {stack.GetText(-1)}");
}
