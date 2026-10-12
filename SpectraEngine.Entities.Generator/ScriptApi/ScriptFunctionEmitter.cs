using System.Collections.Generic;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Writes the method that stands between a script and one typed C# method:
// it checks each argument, converts it, calls, and pushes what comes back.
internal static class ScriptFunctionEmitter
{
    private const string Signature = ScriptNames.Context + " context, " + ScriptNames.Stack + " stack";

    public static string GlueName(ScriptFunctionModel function) => $"__{function.LuauName}";

    // The call of a method of a class or a value, with `self` as the object.
    public static string CallOnSelf(ScriptFunctionModel function) => function.IsByStack
        ? string.Format(function.CallFormat, "self")
        : $"{GlueName(function)}(context, stack, self)";

    // The call of a function of a library or a global function.
    public static string CallFree(ScriptFunctionModel function) => function.IsByStack
        ? function.CallFormat
        : $"{GlueName(function)}(context, stack)";

    // `self` is null for a function that is called on no object. Its
    // arguments then start at the first value, and at the second otherwise.
    public static void EmitGlue(SourceWriter w, ScriptFunctionModel function, ScriptTypeModel? self, bool isStatic)
    {
        string selfParameter = self is null ? "" : $", {self.ClrType} self";
        w.Open($"private {(isStatic ? "static " : "")}int {GlueName(function)}({Signature}{selfParameter})");

        int first = self is null ? 1 : 2;
        var arguments = new List<string>();
        if (function.TakesSelf)
            arguments.Add("self");

        for (int n = 0; n < function.Parameters.Count; n++)
        {
            ScriptParameterModel parameter = function.Parameters[n];
            ScriptMarshalling.EmitRead(w, parameter, first + n, n, function.LuauName, isMethod: self is not null);
            arguments.Add(ScriptMarshalling.Argument(parameter, n));
        }

        if (function.Parameters.Count > 0)
            w.Line();

        string call = $"{string.Format(function.CallFormat, "self")}{string.Join(", ", arguments)})";
        ScriptMarshalling.EmitCallAndReturn(w, call, function.Returns);
        w.Close();
        w.Line();
    }
}
