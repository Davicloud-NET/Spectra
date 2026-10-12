using System.Collections.Generic;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// The C# that takes a value off the stack and puts one on it, for each kind
// of value. The readers it names are the binding's, and each answers false
// for a value of another kind.
internal static class ScriptMarshalling
{
    // Reads the argument at a stack index into the local a{n}. A value of
    // the wrong kind returns the script's error from the generated method.
    public static void EmitRead(
        SourceWriter w, ScriptParameterModel parameter, int index, int n, string function, bool isMethod)
    {
        string expected = parameter.Type.LuauType.TrimEnd('?');
        string wrong =
            $"return {ScriptNames.Arguments}.Wrong(stack, {index}, {ScriptNames.Literal(function)}, " +
            $"{ScriptNames.Literal(expected)}, isMethod: {(isMethod ? "true" : "false")});";

        if (!parameter.IsOptional)
        {
            w.Line($"if (!{Reader(parameter.Type, index, $"a{n}")})");
            w.Indent().Line(wrong).Outdent();
            return;
        }

        w.Line($"{LocalType(parameter)} a{n} = {parameter.Default};");
        w.Open($"if (!stack.IsNil({index}))");
        w.Line($"if (!{Reader(parameter.Type, index, $"r{n}")})");
        w.Indent().Line(wrong).Outdent();
        w.Line($"a{n} = r{n};");
        w.Close();
    }

    // The local as the C# method takes it. A number is read as a double.
    public static string Argument(ScriptParameterModel parameter, int n)
    {
        if (parameter.Type.Kind != ScriptValueKind.Number)
            return $"a{n}";

        bool isNullable = parameter.IsOptional && parameter.Default == "null";
        return $"({parameter.Type.ClrType}{(isNullable ? "?" : "")})a{n}";
    }

    // The condition that reads the object a method is called on, into `self`.
    public static string ReadSelf(ScriptTypeModel self) => Reader(self, 1, "self");

    // An expression that pushes the value and is true when it was pushed.
    // `n` keeps the names apart when several are pushed in one statement.
    public static string Push(ScriptTypeModel type, string value, int n = 0)
    {
        string pushed = $"pushed{n}";
        string push = type.Kind switch
        {
            ScriptValueKind.Number => $"stack.Push((double){(type.IsNullable ? pushed : $"({value})")})",
            ScriptValueKind.CFrame => $"{ScriptNames.FrameValue}.Push(stack, {(type.IsNullable ? pushed : value)})",
            ScriptValueKind.Object => $"context.PushObject(stack, {value})",
            _ => $"stack.Push({(type.IsNullable ? pushed : value)})",
        };

        return type.IsNullable && type.Kind != ScriptValueKind.Object
            ? $"(({value}) is {{ }} {pushed} ? {push} : stack.PushNil())"
            : push;
    }

    // The statements that call a typed method and push what it returns.
    public static void EmitCallAndReturn(SourceWriter w, string call, IReadOnlyList<ScriptTypeModel> returns)
    {
        if (returns.Count == 0)
        {
            w.Line($"{call};");
            w.Line("return 0;");
            return;
        }

        if (returns.Count == 1)
        {
            w.Line($"return {Push(returns[0], call)} ? 1 : {ScriptNames.Raise};");
            return;
        }

        w.Line($"var result = {call};");
        var pushes = new List<string>(returns.Count);
        for (int i = 0; i < returns.Count; i++)
            pushes.Add(Push(returns[i], $"result.Item{i + 1}", i));

        w.Line($"return {string.Join(" && ", pushes)} ? {returns.Count} : {ScriptNames.Raise};");
    }

    private static string Reader(ScriptTypeModel type, int index, string local) => type.Kind switch
    {
        ScriptValueKind.Number => $"{ScriptNames.Arguments}.TryDouble(stack, {index}, out double {local})",
        ScriptValueKind.Boolean => $"{ScriptNames.Arguments}.TryBoolean(stack, {index}, out bool {local})",
        ScriptValueKind.Text => $"{ScriptNames.Arguments}.TryText(stack, {index}, out string? {local})",
        ScriptValueKind.Utf8 => $"{ScriptNames.Arguments}.TryUtf8(stack, {index}, out {type.ClrType} {local})",
        ScriptValueKind.Vector3 => $"{ScriptNames.Arguments}.TryVector(stack, {index}, out {type.ClrType} {local})",
        ScriptValueKind.CFrame => $"{ScriptNames.Arguments}.TryFrame(stack, {index}, out {type.ClrType} {local})",
        _ => $"context.TryGetObject(stack, {index}, out {type.ClrType}? {local})",
    };

    // The type of the local an optional argument is kept in.
    private static string LocalType(ScriptParameterModel parameter)
    {
        string type = parameter.Type.Kind switch
        {
            ScriptValueKind.Number => "double",
            ScriptValueKind.Boolean => "bool",
            ScriptValueKind.Text => "string",
            _ => parameter.Type.ClrType,
        };

        bool isReference = parameter.Type.Kind is ScriptValueKind.Text or ScriptValueKind.Object;
        return parameter.Default == "null" || isReference ? type + "?" : type;
    }
}
