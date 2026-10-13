using System.Text;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// SetAttribute, GetAttribute and their relatives. What a script sets is kept
// in the table and read from there. A name no script has set is a keyvalue
// of the node's entity, which the entity itself goes on reading: nothing a
// script sets here reaches it.
internal sealed class AttributeMethods
{
    // In bytes of UTF-8. A longer name gets an array of its own.
    private const int NameRoom = 512;

    // What a script reads when the bytes it handed over as a name are no text.
    private const string NotText =
        "A name that is not valid UTF-8 is not a valid attribute name. " + AttributeName.Rule;

    private readonly SignalHub _hub;
    private readonly DeferredCalls _calls;
    private readonly List<string> _built = [];

    // What every name no attribute can have gets as its signal.
    private ScriptSignal? _neverFires;

    public AttributeMethods(SignalHub hub, DeferredCalls calls)
    {
        _hub = hub;
        _calls = calls;
    }

    // What scripts set. Setting here fires nothing, which is how a level's
    // own attributes are put in before its scripts start.
    public AttributeTable Table { get; } = new();

    // SetAttribute. The name is the second value and what to set the third.
    public int Set(ScriptContext context, LuauStack stack, SceneNode node)
    {
        if (!stack.TryGetUtf8(2, out ReadOnlySpan<byte> utf8))
            return ScriptArguments.Wrong(stack, 2, "SetAttribute", "string", isMethod: true);

        Span<char> room = stackalloc char[NameChars.Room];
        if (!NameChars.TryRead(utf8, room, out ReadOnlySpan<char> name))
            return stack.Fail(NotText);
        if (!AttributeName.IsValid(name))
            return stack.Fail(AttributeName.Refusal(name));

        bool wasSet = Table.TryGet(node, name, out MemberValue held);
        if (!AttributeValues.TryRead(stack, 3, in held, out MemberValue value, out string? refusal))
            return stack.Fail(refusal);

        // The value it has already is set to nothing and fires nothing.
        if (wasSet ? held == value : IsWhatWasBuilt(context, node, name, in value))
            return 0;

        // From here a name the node does not hold is one more for it to hold.
        if (!wasSet && Table.Of(node) is { Values.Count: >= AttributeLimits.MaxPerNode })
        {
            return stack.Fail(
                $"\"{node.Name}\" has {AttributeLimits.MaxPerNode} attributes, and a node holds no more. " +
                "SetAttribute with nil takes one away");
        }

        // Taken away from a keyvalue, the name is kept with no value, or
        // the keyvalue would show through again.
        string kept = Table.NameOf(name);
        if (value.Kind == MemberValueKind.None && !AttributeRead.TryRead(context, node, name, out _, out _))
            Table.Forget(node, name);
        else
            Table.Set(node, kept, in value);

        if (TreeWrites.IsDestroyed(node))
            context.Lifetime?.DestroyedChanged(node);

        FireChanged(node, kept);
        return 0;
    }

    // GetAttribute. The name is the second value.
    public int Get(ScriptContext context, LuauStack stack, SceneNode node)
    {
        if (!stack.TryGetUtf8(2, out ReadOnlySpan<byte> utf8))
            return ScriptArguments.Wrong(stack, 2, "GetAttribute", "string", isMethod: true);

        // The name is compared where it lies, so asking makes no string.
        Span<char> room = stackalloc char[NameChars.Room];
        if (!NameChars.TryRead(utf8, room, out ReadOnlySpan<char> name))
            return ScriptResult.Of(stack.PushNil());
        if (Table.TryGet(node, name, out MemberValue set))
            return ScriptResult.Of(AttributeValues.Push(stack, in set));

        return AttributeRead.TryRead(context, node, name, out MemberValue built, out SceneNode? reference)
            ? ScriptResult.Of(AttributeRead.Push(context, stack, in built, reference))
            : ScriptResult.Of(stack.PushNil());
    }

    // GetAttributes: the keyvalues of the node's entity, under what scripts set.
    public int GetAll(ScriptContext context, LuauStack stack, SceneNode node)
    {
        Dictionary<string, MemberValue>? set = Table.Of(node)?.Values;
        if (!stack.NewTable(0, set?.Count ?? 0))
            return LuauStack.Raise;

        int table = stack.Count;
        _built.Clear();
        AuthoredAttributes.AddNames(node, context.World.Catalog, _built);
        foreach (string name in _built)
        {
            // A name a script set or took away is the script's from then on.
            if (set?.ContainsKey(name) == true)
                continue;

            // One that reads as nil is no entry: a table holds no nil.
            if (!AttributeRead.TryRead(context, node, name, out MemberValue built, out SceneNode? reference)
                || (reference is null && built.Kind == MemberValueKind.None))
            {
                continue;
            }

            if (!AttributeRead.Push(context, stack, in built, reference) || !SetNamed(stack, table, name))
                return LuauStack.Raise;
        }

        if (set is null)
            return 1;

        foreach (KeyValuePair<string, MemberValue> pair in set)
        {
            if (pair.Value.Kind == MemberValueKind.None)
                continue;
            if (!AttributeValues.Push(stack, pair.Value) || !SetNamed(stack, table, pair.Key))
                return LuauStack.Raise;
        }

        return 1;
    }

    // AttributeChanged: one signal for a node, so a script reads the same
    // object every time.
    // A node that was destroyed gets the signal that never fires: it still
    // takes an attribute, and tells nobody.
    public ScriptSignal ChangedSignal(SceneNode node) =>
        TreeWrites.IsDestroyed(node)
            ? _neverFires ??= new ScriptSignal("Signal AttributeChanged", _hub)
            : Table.OfOrMake(node).Changed ??= new ScriptSignal("Signal AttributeChanged", _hub);

    // GetAttributeChangedSignal. The name is the second value. A name no
    // attribute can have is never set, so its signal never fires. It is one
    // signal for all such names, and nothing is kept for the name.
    public int PushChangedSignalOf(ScriptContext context, LuauStack stack, SceneNode node)
    {
        if (!stack.TryGetUtf8(2, out ReadOnlySpan<byte> utf8))
            return ScriptArguments.Wrong(stack, 2, "GetAttributeChangedSignal", "string", isMethod: true);

        // A name is ASCII, so more bytes than a name has characters is no name.
        Span<char> room = stackalloc char[NameChars.Room];
        if (utf8.Length > AttributeName.MaxLength
            || !NameChars.TryRead(utf8, room, out ReadOnlySpan<char> name)
            || !AttributeName.IsValid(name)
            || TreeWrites.IsDestroyed(node))
        {
            _neverFires ??= new ScriptSignal("Signal AttributeChanged", _hub);
            return ScriptResult.Of(context.PushObject(stack, _neverFires));
        }

        NodeAttributes own = Table.OfOrMake(node);
        own.ChangedOf ??= new Dictionary<string, ScriptSignal>(StringComparer.Ordinal);
        if (!own.ChangedOf.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out ScriptSignal? signal))
        {
            if (own.ChangedOf.Count >= AttributeLimits.MaxPerNode)
            {
                return stack.Fail(
                    $"\"{node.Name}\" keeps the signals of {AttributeLimits.MaxPerNode} attribute names, " +
                    "and a node keeps no more");
            }

            signal = new ScriptSignal("Signal AttributeChanged", _hub);
            own.ChangedOf.Add(Table.NameOf(name), signal);
        }

        return ScriptResult.Of(context.PushObject(stack, signal));
    }

    // Whether the node reads as that value with nothing set by a script: its
    // entity's keyvalue, or nil. A reference to a node is no value a script sets.
    private static bool IsWhatWasBuilt(
        ScriptContext context, SceneNode node, ReadOnlySpan<char> name, in MemberValue value)
    {
        AttributeRead.TryRead(context, node, name, out MemberValue built, out SceneNode? reference);
        return reference is null && built == value;
    }

    // A listener of AttributeChanged is handed the name. One that asked for
    // the one name is handed nothing.
    private void FireChanged(SceneNode node, string name)
    {
        if (Table.Of(node) is not { } own)
            return;

        if (own.Changed is { ListenerCount: > 0 } changed)
            _calls.Fire(changed, name);
        if (own.ChangedOf is { } byName && byName.TryGetValue(name, out ScriptSignal? named) && named.ListenerCount > 0)
            _calls.Fire(named);
    }

    // Sets the field of that name to the value on top, and takes the value.
    private static bool SetNamed(LuauStack stack, int table, string name)
    {
        int most = Encoding.UTF8.GetMaxByteCount(name.Length);
        Span<byte> utf8 = most <= NameRoom ? stackalloc byte[NameRoom] : new byte[most];
        return stack.SetField(table, utf8[..Encoding.UTF8.GetBytes(name, utf8)]);
    }
}
