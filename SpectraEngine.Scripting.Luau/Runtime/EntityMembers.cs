using System.Text;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The members a node has from its entity class: an input is a method and an
// output is a signal. A class is data, so they are found by name in what
// EntityClassApi reads from its schema. ObjectMembers asks here after the
// node's own class and before it looks for a child.
internal sealed class EntityMembers
{
    private const string NoRoom =
        "was not sent: the level's scripts have too much waiting in its queue";

    private readonly LuauHost _host;
    private readonly EntityWorld _world;
    private readonly NodeSignals _signals;

    // By the class and by what the node carries, whose own members win.
    // Null for a class this level cannot build, whose node is a placeholder.
    private readonly Dictionary<(string Class, NodeClassKind Carrier), EntityClassMembers?> _classes = [];

    // The function a script gets when it reads an input and does not call
    // it: one for each name, made on the first read.
    private readonly Dictionary<string, LuauReference> _methods = new(StringComparer.Ordinal);

    public EntityMembers(LuauHost host, EntityWorld world, NodeSignals signals)
    {
        _host = host;
        _world = world;
        _signals = signals;
    }

    // Whether the node carries an entity of a class this level cannot build,
    // so nothing is known of its inputs and outputs.
    public bool IsOfMissingClass(SceneNode node) =>
        node.Entity is { ClassName.Length: > 0 } && ClassOf(node) is null;

    // node.Name, with the name as the second value. ScriptMember.None when
    // the node's entity class has no input or output of that name.
    public int Read(ScriptContext context, LuauStack stack, SceneNode node)
    {
        if (ClassOf(node) is not { } members || !stack.TryGetUtf8(2, out ReadOnlySpan<byte> utf8))
            return ScriptMember.None;

        Span<char> room = stackalloc char[NameChars.Room];
        if (!NameChars.TryRead(utf8, room, out ReadOnlySpan<char> name))
            return ScriptMember.None;

        if (members.TryGetInput(name, out EntityPort input))
            return PushMethod(stack, input.Name);

        return members.TryGetOutput(name, out EntityPort output)
            ? ScriptResult.Of(context.PushObject(stack, _signals.OfOutput(node, output)))
            : ScriptMember.None;
    }

    // node.Name = value. An input and an output are members a script cannot set.
    public int Write(LuauStack stack, SceneNode node)
    {
        if (ClassOf(node) is not { } members || !stack.TryGetUtf8(2, out ReadOnlySpan<byte> utf8))
            return ScriptMember.None;

        Span<char> room = stackalloc char[NameChars.Room];
        return NameChars.TryRead(utf8, room, out ReadOnlySpan<char> name)
            && (members.TryGetInput(name, out _) || members.TryGetOutput(name, out _))
                ? ScriptMember.ReadOnly
                : ScriptMember.None;
    }

    // node:Name(...). An input is sent. An output is read and is then no function.
    public int Invoke(ScriptContext context, LuauStack stack, SceneNode node)
    {
        if (ClassOf(node) is not { } members)
            return ScriptMember.None;

        Span<char> room = stackalloc char[NameChars.Room];
        if (!NameChars.TryRead(stack.NamecallUtf8, room, out ReadOnlySpan<char> name))
            return ScriptMember.None;

        if (members.TryGetInput(name, out EntityPort input))
            return Send(stack, node, input);
        if (!members.TryGetOutput(name, out EntityPort output))
            return ScriptMember.None;

        return context.PushObject(stack, _signals.OfOutput(node, output))
            ? ScriptMember.FailNotCallable(stack)
            : LuauStack.Raise;
    }

    private EntityClassMembers? ClassOf(SceneNode node)
    {
        if (node.Entity is not { ClassName.Length: > 0 } data)
            return null;

        NodeClassKind carrier = NodeClass.KindOf(node);
        return _classes.TryGetValue((data.ClassName, carrier), out EntityClassMembers? known)
            ? known
            : Learn(data.ClassName, carrier);
    }

    // The catalogue that builds the level's entities, so a class is to a
    // script what it is to the level. What of the class no script reaches on
    // such a node is said here, once.
    private EntityClassMembers? Learn(string className, NodeClassKind kind)
    {
        EntityClassMembers? members = null;
        if (_world.Catalog.TryGetSchema(className, out EntitySchema? schema))
        {
            string carrier = NodeClass.NameOf(kind);
            members = EntityClassApi.MembersOf(schema, LuauScriptApi.Description, carrier);
            foreach (string gap in EntityClassGaps.OfPorts(schema, LuauScriptApi.Description, carrier))
                _host.Report(new ScriptMessage(ScriptSeverity.Warning, gap, string.Empty, 0));
        }

        _classes.Add((className, kind), members);
        return members;
    }

    // Queues the input for the node's entity, as a wire would. The node is
    // the first value and what the input takes follows it.
    private int Send(LuauStack stack, SceneNode node, EntityPort input)
    {
        if (!_world.IsActive)
            return stack.Fail($"{input.Name} was not sent: the level has stopped");

        if (_world.Index is not { } index
            || !index.TryGetByNodeId(node.Id, out Entity? entity)
            || !ReferenceEquals(entity.Node, node))
        {
            return stack.Fail($"{input.Name} was not sent: the entity of \"{node.Name}\" is not running");
        }

        // A nil at the end is a value left out.
        int count = stack.Count - 1;
        while (count > 0 && stack.IsNil(count + 1))
            count--;

        if (count > WireValues.MostValues(input.Parameter))
            return WireValues.TooMany(stack, input, count);

        int read = WireValues.Read(_host, stack, input, count, out ParameterValue value);
        if (read != 0)
            return read;
        if (!ParameterText.TryFormat(input.Parameter, in value, out string text))
            return stack.Fail($"{input.Name} was not sent: no wire can carry that value");

        return Queue(stack, entity, input.Name, text) ? 0 : stack.Fail($"{input.Name} {NoRoom}");
    }

    // Only a trace reads who sent an input, so only a watched level looks
    // the place up. The name of a module's file is a new string each time.
    private bool Queue(LuauStack stack, Entity entity, string input, string text)
    {
        if (_world.Trace is null)
            return _world.QueueScriptInput(entity, input, text, string.Empty, 0);

        ScriptMessage place = ScriptReport.Line(stack.State, _host.ScriptOf(stack), ScriptSeverity.Print, string.Empty);
        return _world.QueueScriptInput(entity, input, text, place.Script, place.Line);
    }

    private int PushMethod(LuauStack stack, string input) =>
        _methods.TryGetValue(input, out LuauReference? kept) ? ScriptResult.Of(kept.Push(stack)) : MakeMethod(stack, input);

    // Apart from PushMethod: the function it makes holds the name, and a
    // read that finds the function made must make nothing.
    private int MakeMethod(LuauStack stack, string input)
    {
        if (!_host.PushFunction(stack, Encoding.UTF8.GetBytes(input), called => SendByName(called, input)))
            return LuauStack.Raise;
        if (_host.Keep(stack, -1) is not { } made)
            return stack.Fail($"the method {input} was not set up");

        _methods.Add(input, made);
        return 1;
    }

    // node.Name(node, ...): the function of an input, called with a dot. The
    // node it is handed may be any, so the name is looked up again.
    private int SendByName(LuauStack stack, string input)
    {
        if (_host.Objects.TryGet(stack, 1, ScriptTags.Object, out object? held) != LuauObjectLookup.Found
            || held is not SceneNode node)
        {
            return ScriptArguments.NoSelf(stack, input);
        }

        return ClassOf(node) is { } members && members.TryGetInput(input, out EntityPort found)
            ? Send(stack, node, found)
            : stack.Fail($"{input} is not a valid member of {NodeClassName.Of(node)} \"{node.Name}\"");
    }
}
