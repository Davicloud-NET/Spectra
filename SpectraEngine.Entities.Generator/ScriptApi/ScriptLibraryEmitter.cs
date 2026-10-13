using System.Linq;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Writes what puts a library or the globals of a class into a Luau state:
// one method that makes the table and sets it, or sets each global. What is
// bound by hand is left to the hand.
internal static class ScriptLibraryEmitter
{
    public static void Emit(SourceWriter w, ScriptContainerModel model)
    {
        ScriptPropertyModel[] values = model.Properties.Where(property => !property.IsByHand).ToArray();
        ScriptFunctionModel[] functions = model.Functions.Where(function => !function.IsByHand).ToArray();
        if (values.Length + functions.Length == 0)
            return;

        // A member of an object is reached through `this`.
        string modifier = model.IsStatic ? "static " : "";
        w.Line("/// <summary>Puts what this class declares into the state. Once, before the host is sealed.</summary>");
        w.Open($"internal {modifier}void ScriptInstall({ScriptNames.Context} context)");
        if (model.Kind == ScriptContainerKind.Library)
            EmitTable(w, model, values, functions);
        else
            EmitGlobals(w, values, functions);

        w.Close();
        w.Line();

        foreach (ScriptFunctionModel function in functions.Where(function => !function.IsByStack))
            ScriptFunctionEmitter.EmitGlue(w, function, self: null, isStatic: function.IsStatic);
    }

    private static void EmitTable(
        SourceWriter w, ScriptContainerModel model, ScriptPropertyModel[] constants, ScriptFunctionModel[] functions)
    {
        w.Line($"new {ScriptNames.ApiTable}(context.Host)");
        w.Indent();
        foreach (ScriptPropertyModel constant in constants)
        {
            string read = constant.ReadFormat;
            string put = constant.Type.Kind switch
            {
                ScriptValueKind.CFrame => $".Frame({ScriptNames.Utf8(constant.LuauName)}, {read})",
                ScriptValueKind.Color3 => $".Color({ScriptNames.Utf8(constant.LuauName)}, {read})",
                ScriptValueKind.Number => $".Value({ScriptNames.Utf8(constant.LuauName)}, (double)({read}))",
                _ => $".Value({ScriptNames.Utf8(constant.LuauName)}, {read})",
            };
            w.Line(put);
        }

        foreach (ScriptFunctionModel function in functions)
            w.Line($".Function({ScriptNames.Utf8(function.LuauName)}, stack => {ScriptFunctionEmitter.CallFree(function)})");

        w.Line($".SetAsGlobal({ScriptNames.Utf8(model.LuauName)});");
        w.Outdent();
    }

    private static void EmitGlobals(SourceWriter w, ScriptPropertyModel[] values, ScriptFunctionModel[] functions)
    {
        foreach (ScriptPropertyModel value in values)
            w.Line($"context.SetGlobalObject({ScriptNames.Utf8(value.LuauName)}, {value.ReadFormat});");

        foreach (ScriptFunctionModel function in functions)
        {
            w.Line(
                $"context.SetGlobalFunction({ScriptNames.Utf8(function.LuauName)}, " +
                $"stack => {ScriptFunctionEmitter.CallFree(function)});");
        }
    }
}
