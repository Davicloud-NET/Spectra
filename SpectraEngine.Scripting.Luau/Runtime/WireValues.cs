using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The value of an input or an output between a script and the text a wire
// carries. ParameterText does the text. This is the script's side: a
// boolean, a number, a string, a Vector3, numbers or a node, by what
// EntityClassApi says a type is to a script.
internal static class WireValues
{
    // The first value after the node an input is called on.
    private const int First = 2;

    // Calls a listener with what an output sent. Nothing for an output that
    // sends nothing, and for text its type cannot read. That is the one case
    // that answers false.
    public static bool Hand(ScriptConnection connection, string parameter, Scene scene)
    {
        ParameterType sends = connection.Sends;

        // A schema that does not say what the text is hands the text over.
        if (!sends.IsSaid && parameter.Length > 0)
        {
            connection.Run([parameter]);
            return true;
        }

        if (!sends.HasValue)
        {
            connection.Run([]);
            return true;
        }

        if (!ParameterText.TryParse(sends, parameter, out ParameterValue value))
        {
            connection.Run([]);
            return false;
        }

        PortValueKind kind = EntityClassApi.KindOf(sends.ValueType);
        if (kind == PortValueKind.Node)
            connection.Run([NodeOf(scene, in value)]);
        else if (value.Kind == ParameterValueKind.None)
            connection.Run([]);
        else
            HandValue(connection, kind, in value);

        return true;
    }

    // How many values an input takes at most.
    public static int MostValues(ParameterType takes)
    {
        if (!takes.IsSaid)
            return 1;
        if (!takes.HasValue)
            return 0;

        return EntityClassApi.KindOf(takes.ValueType) switch
        {
            PortValueKind.TwoNumbers => 2,
            PortValueKind.FourNumbers => 4,
            _ => 1,
        };
    }

    // The error for more values than an input takes.
    public static int TooMany(LuauStack stack, EntityPort input, int count)
    {
        string got = count == 1 ? "1 value" : string.Create(CultureInfo.InvariantCulture, $"{count} values");
        return stack.Fail($"too many arguments to '{input.Name}' ({Expected(input.Parameter)} expected, got {got})");
    }

    // Reads the `count` values a script handed an input, as the one value a
    // wire carries. Zero when they were read. Anything else is the script's
    // error, for the host function to return as it is.
    public static int Read(LuauHost host, LuauStack stack, EntityPort input, int count, out ParameterValue value)
    {
        value = ParameterValue.None;
        ParameterType takes = input.Parameter;

        if (!takes.IsSaid)
            return count == 0 ? 0 : ReadAny(host, stack, input.Name, out value);
        if (!takes.HasValue)
            return 0;

        PortValueKind kind = EntityClassApi.KindOf(takes.ValueType);
        if (count == 0)
            return takes.IsOptional || kind == PortValueKind.Node ? 0 : Wrong(stack, First, input.Name, TypeOf(kind));

        return kind switch
        {
            PortValueKind.Boolean => ReadBoolean(stack, input.Name, out value),
            PortValueKind.Number => ReadNumber(stack, input.Name, isWhole: false, out value),
            PortValueKind.WholeNumber => ReadNumber(stack, input.Name, isWhole: true, out value),
            PortValueKind.Text => ReadText(stack, input.Name, out value),
            PortValueKind.Vector3 => ReadVector(stack, input.Name, out value),
            PortValueKind.TwoNumbers => ReadNumbers(stack, input.Name, 2, out value),
            PortValueKind.FourNumbers => ReadNumbers(stack, input.Name, 4, out value),
            _ => ReadNode(host, stack, input.Name, out value),
        };
    }

    private static void HandValue(ScriptConnection connection, PortValueKind kind, in ParameterValue value)
    {
        Vector4 v = value.Vector;
        switch (kind)
        {
            case PortValueKind.Boolean:
                connection.Run([value.Boolean]);
                break;
            case PortValueKind.Number or PortValueKind.WholeNumber:
                connection.Run([value.Number]);
                break;
            case PortValueKind.Text:
                connection.Run([value.Text]);
                break;
            case PortValueKind.Vector3:
                connection.Run([new Vector3(v.X, v.Y, v.Z)]);
                break;
            case PortValueKind.TwoNumbers:
                connection.Run([(double)v.X, (double)v.Y]);
                break;
            default:
                connection.Run([(double)v.X, (double)v.Y, (double)v.Z, (double)v.W]);
                break;
        }
    }

    // Nil for no node, and for one that has left the level since the fire.
    private static LuauValue NodeOf(Scene scene, in ParameterValue value) =>
        value.Kind == ParameterValueKind.NodeId && scene.TryFindById(value.NodeId, out SceneNode? node)
            ? LuauValue.Object(ScriptTags.Object, node)
            : LuauValue.Nil;

    private static int ReadBoolean(LuauStack stack, string input, out ParameterValue value)
    {
        value = ParameterValue.None;
        if (!ScriptArguments.TryBoolean(stack, First, out bool flag))
            return Wrong(stack, First, input, "boolean");

        value = ParameterValue.Of(flag);
        return 0;
    }

    // A wire carries a float, and a counter's step a whole number an int holds.
    private static int ReadNumber(LuauStack stack, string input, bool isWhole, out ParameterValue value)
    {
        value = ParameterValue.None;
        if (!stack.TryGetNumber(First, out double number))
            return Wrong(stack, First, input, "number");

        bool isCarried = isWhole
            ? number == Math.Truncate(number) && number is >= int.MinValue and <= int.MaxValue
            : float.IsFinite((float)number);
        if (!isCarried)
            return WrongNumber(stack, First, input, isWhole ? "a whole number" : "a finite number", number);

        value = ParameterValue.Of(number);
        return 0;
    }

    private static int ReadText(LuauStack stack, string input, out ParameterValue value)
    {
        value = ParameterValue.None;

        // Not a number: reading one as text would turn it into a string where it sits.
        if (stack.TypeOf(First) != LuauType.String || stack.GetText(First) is not { } text)
            return Wrong(stack, First, input, "string");

        value = ParameterValue.Of(text);
        return 0;
    }

    private static int ReadVector(LuauStack stack, string input, out ParameterValue value)
    {
        value = ParameterValue.None;
        if (!stack.TryGetVector(First, out Vector3 vector))
            return Wrong(stack, First, input, "Vector3");
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
            return stack.Fail($"invalid argument #1 to '{input}' (a Vector3 of finite numbers expected)");

        value = ParameterValue.Of(vector);
        return 0;
    }

    private static int ReadNumbers(LuauStack stack, string input, int size, out ParameterValue value)
    {
        value = ParameterValue.None;
        Span<float> parts = stackalloc float[4];
        for (int i = 0; i < size; i++)
        {
            if (!stack.TryGetNumber(First + i, out double number))
                return Wrong(stack, First + i, input, "number");
            if (!float.IsFinite((float)number))
                return WrongNumber(stack, First + i, input, "a finite number", number);

            parts[i] = (float)number;
        }

        value = size == 2
            ? ParameterValue.Of(new Vector2(parts[0], parts[1]))
            : ParameterValue.Of(new Vector4(parts[0], parts[1], parts[2], parts[3]));
        return 0;
    }

    private static int ReadNode(LuauHost host, LuauStack stack, string input, out ParameterValue value)
    {
        value = ParameterValue.None;
        if (stack.IsNil(First))
            return 0;
        if (NodeAt(host, stack, First) is not { } node)
            return Wrong(stack, First, input, "Instance");

        value = ParameterValue.Of(node.Id);
        return 0;
    }

    // A class that does not say what its input takes gets the value written
    // as a person would type it on a wire.
    private static int ReadAny(LuauHost host, LuauStack stack, string input, out ParameterValue value)
    {
        value = ParameterValue.None;
        switch (stack.TypeOf(First))
        {
            case LuauType.Boolean:
                return ReadBoolean(stack, input, out value);
            case LuauType.Number:
                return ReadNumber(stack, input, isWhole: false, out value);
            case LuauType.String:
                return ReadText(stack, input, out value);
            case LuauType.Vector:
                return ReadVector(stack, input, out value);
            default:
                return NodeAt(host, stack, First) is null
                    ? Wrong(stack, First, input, "boolean, number, string, Vector3 or Instance")
                    : ReadNode(host, stack, input, out value);
        }
    }

    private static SceneNode? NodeAt(LuauHost host, LuauStack stack, int index) =>
        host.Objects.TryGet(stack, index, ScriptTags.Object, out object? held) == LuauObjectLookup.Found
            ? held as SceneNode
            : null;

    private static int Wrong(LuauStack stack, int index, string input, string expected) =>
        ScriptArguments.Wrong(stack, index, input, expected, isMethod: true);

    private static int WrongNumber(LuauStack stack, int index, string input, string expected, double got) =>
        stack.Fail(string.Create(
            CultureInfo.InvariantCulture, $"invalid argument #{index - 1} to '{input}' ({expected} expected, got {got:R})"));

    // What an input takes, in the words of an error: "no value", "number",
    // "number, number".
    private static string Expected(ParameterType takes)
    {
        if (!takes.IsSaid)
            return "one value";
        if (!takes.HasValue)
            return "no value";

        IReadOnlyList<ScriptParameter> values = EntityClassApi.ValuesOf(takes);
        return string.Join(", ", values.Select(value => value.Type));
    }

    private static string TypeOf(PortValueKind kind) => kind switch
    {
        PortValueKind.Boolean => "boolean",
        PortValueKind.Text => "string",
        PortValueKind.Vector3 => "Vector3",
        PortValueKind.Node => "Instance",
        _ => "number",
    };
}
