using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The one metatable every managed object shares: a node, a service, a
// signal, a connection. The members are in the generated glue of each
// class. This finds the object, and words what a script reads for a name
// the object does not have. That is an error, never nil. On a node, a name
// that is no member gives the child of that name.
internal sealed class ObjectMembers
{
    private readonly ScriptContext _context;

    private ObjectMembers(ScriptContext context) => _context = context;

    public static void Install(ScriptContext context)
    {
        ObjectMembers members = new(context);

        new ApiTable(context.Host)
            .Function("__index"u8, members.Index)
            .Function("__newindex"u8, members.NewIndex)
            .Function("__namecall"u8, members.NameCall)
            .Function("__tostring"u8, members.ToText)
            .Value("__type"u8, "Instance")
            .Value("__metatable"u8, "The metatable is locked")
            .SetAsObjectMetatable(ScriptTags.Object);
    }

    private int Index(LuauStack stack)
    {
        if (TargetOf(stack) is not { } target)
            return stack.Fail(ScriptMember.HasLeft);
        if (!stack.TryGetAtom(2, out int atom))
            return stack.Fail(NotAName(stack));

        int read = Read(stack, target, atom);
        if (read != ScriptMember.None)
            return read;

        // A name that is no member is a child's, as in Roblox. Looked for
        // only here, so a member always wins and costs what it did.
        return target is SceneNode parent && ChildNamed(stack, parent) is { } child
            ? ScriptResult.Of(_context.PushObject(stack, child))
            : stack.Fail(NotAMember(stack.GetText(2), target));
    }

    private int NewIndex(LuauStack stack)
    {
        if (TargetOf(stack) is not { } target)
            return stack.Fail(ScriptMember.HasLeft);
        if (!stack.TryGetAtom(2, out int atom))
            return stack.Fail(NotAName(stack));

        int written = target is SceneNode node
            ? NodeMembers.Write(_context, stack, node, atom)
            : ((ScriptObject)target).WriteMember(_context, stack, atom);

        return written switch
        {
            ScriptMember.None => stack.Fail(NotAMember(stack.GetText(2), target)),
            ScriptMember.ReadOnly => stack.Fail($"Unable to assign property {stack.GetText(2)}. Property is read only"),
            _ => written,
        };
    }

    // object:Method(...). Luau hands over the method as an atom, so this
    // makes no string and looks nothing up.
    private int NameCall(LuauStack stack)
    {
        if (TargetOf(stack) is not { } target)
            return stack.Fail(ScriptMember.HasLeft);

        int atom = stack.NamecallAtom;
        int called = target is SceneNode node
            ? NodeMembers.Invoke(_context, stack, node, atom)
            : ((ScriptObject)target).InvokeMember(_context, stack, atom);

        if (called == ScriptMember.None)
            return FailNoMethod(stack, target);
        if (called != ScriptMember.NotCallable)
            return called;

        // A property called like a method: the value is read, and then it is no function.
        int read = Read(stack, target, atom);
        return read == 1 ? ScriptMember.FailNotCallable(stack) : read;
    }

    private int Read(LuauStack stack, object target, int atom) =>
        target is SceneNode node
            ? NodeMembers.Read(_context, stack, node, atom)
            : ((ScriptObject)target).ReadMember(_context, stack, atom);

    // A child called like a method is read, as a property is, and is then no
    // function. Any other name is no member.
    private int FailNoMethod(LuauStack stack, object target)
    {
        string? name = stack.NamecallName;
        if (target is SceneNode parent && name is not null && NodeTree.Child(parent, NodeMatch.Name, name) is { } child)
            return _context.PushObject(stack, child) ? ScriptMember.FailNotCallable(stack) : LuauStack.Raise;

        return stack.Fail(NotAMember(name, target));
    }

    // The child the second value names, or null.
    private static SceneNode? ChildNamed(LuauStack stack, SceneNode parent)
    {
        Span<char> room = stackalloc char[NameChars.Room];
        return stack.TryGetUtf8(2, out ReadOnlySpan<byte> name) && NameChars.TryRead(name, room, out ReadOnlySpan<char> wanted)
            ? NodeTree.Child(parent, NodeMatch.Name, wanted)
            : null;
    }

    private int ToText(LuauStack stack) =>
        ScriptResult.Of(stack.Push(TargetOf(stack) switch
        {
            SceneNode node => node.Name,
            ScriptObject other => other.Name,
            _ => "an object that has left the level",
        }));

    // Null for an object that has left the level, and for what is no object.
    private object? TargetOf(LuauStack stack) =>
        _context.Host.Objects.TryGet(stack, 1, ScriptTags.Object, out object? target) == LuauObjectLookup.Found
            ? target
            : null;

    private static string NotAName(LuauStack stack) =>
        $"a member is named by a string, not by a {ScriptArguments.TypeName(stack, 2)}";

    // Roblox's words for a name a class does not have.
    private static string NotAMember(string? name, object target) => target is SceneNode node
        ? $"{name} is not a valid member of {NodeClassName.Of(node)} \"{node.Name}\""
        : $"{name} is not a valid member of {((ScriptObject)target).ClassName} \"{((ScriptObject)target).Name}\"";
}
