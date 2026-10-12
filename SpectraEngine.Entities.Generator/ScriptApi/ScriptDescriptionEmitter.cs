using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Writes what a container declares as data: the description the
// definitions file and the reference are written from.
internal static class ScriptDescriptionEmitter
{
    private const string Property = ScriptNames.Api + ".ScriptPropertyDescription";
    private const string Function = ScriptNames.Api + ".ScriptFunctionDescription";
    private const string List = "global::System.Collections.Generic.IReadOnlyList";

    public static void Emit(SourceWriter w, ScriptContainerModel model)
    {
        switch (model.Kind)
        {
            case ScriptContainerKind.Library:
                EmitLibrary(w, model);
                break;
            case ScriptContainerKind.Globals:
                EmitGlobals(w, model);
                break;
            default:
                EmitClass(w, model);
                break;
        }
    }

    private static void EmitClass(SourceWriter w, ScriptContainerModel model)
    {
        w.Line("/// <summary>What this class declares, as data.</summary>");
        w.Line($"internal static {ScriptNames.Api}.ScriptClassDescription ScriptDescription {{ get; }} = new()");
        w.Line("{");
        w.Indent();
        w.Line($"Name = {ScriptNames.Literal(model.LuauName)},");
        w.Line($"Extends = {ScriptNames.Literal(model.Extends)},");
        w.Line($"IsValue = {(model.Kind == ScriptContainerKind.Value ? "true" : "false")},");
        w.Line($"TypeParameters = {ScriptNames.Literal(model.TypeParameters)},");
        w.Line($"ChildType = {ScriptNames.Literal(model.ChildType)},");
        w.Line($"Summary = {ScriptNames.Literal(model.Summary)},");
        EmitProperties(w, "Properties", model.Properties.Where(property => !property.IsSignal));
        EmitFunctions(w, "Methods", model.Functions);
        EmitSignals(w, model.Properties.Where(property => property.IsSignal));
        EmitOperators(w, model.Operators);
        w.Outdent();
        w.Line("};");
        w.Line();
    }

    private static void EmitLibrary(SourceWriter w, ScriptContainerModel model)
    {
        w.Line("/// <summary>What this library declares, as data.</summary>");
        w.Line($"internal static {ScriptNames.Api}.ScriptLibraryDescription ScriptDescription {{ get; }} = new()");
        w.Line("{");
        w.Indent();
        w.Line($"Name = {ScriptNames.Literal(model.LuauName)},");
        w.Line($"Summary = {ScriptNames.Literal(model.Summary)},");
        EmitProperties(w, "Constants", model.Properties);
        EmitFunctions(w, "Functions", model.Functions);
        w.Outdent();
        w.Line("};");
        w.Line();
    }

    private static void EmitGlobals(SourceWriter w, ScriptContainerModel model)
    {
        w.Line("/// <summary>The global values this class declares, as data.</summary>");
        w.Line($"internal static {List}<{Property}> ScriptGlobalValues {{ get; }} =");
        EmitList(w, model.Properties.Select(PropertyOf), ";");
        w.Line();
        w.Line("/// <summary>The global functions this class declares, as data.</summary>");
        w.Line($"internal static {List}<{Function}> ScriptGlobalFunctions {{ get; }} =");
        EmitList(w, model.Functions.Select(FunctionOf), ";");
        w.Line();
    }

    private static void EmitProperties(SourceWriter w, string member, IEnumerable<ScriptPropertyModel> properties)
    {
        w.Line($"{member} =");
        EmitList(w, properties.Select(PropertyOf), ",");
    }

    private static void EmitFunctions(SourceWriter w, string member, IEnumerable<ScriptFunctionModel> functions)
    {
        w.Line($"{member} =");
        EmitList(w, functions.Select(FunctionOf), ",");
    }

    private static void EmitSignals(SourceWriter w, IEnumerable<ScriptPropertyModel> signals)
    {
        w.Line("Signals =");
        EmitList(
            w,
            signals.Select(signal =>
                $"new {ScriptNames.Api}.ScriptSignalDescription {{ Name = {ScriptNames.Literal(signal.LuauName)}, " +
                $"Summary = {ScriptNames.Literal(signal.Summary)}, Parameters = {ParametersOf(signal.SignalParameters)} }}"),
            ",");
    }

    private static void EmitOperators(SourceWriter w, IEnumerable<ScriptOperatorModel> operators)
    {
        w.Line("Operators =");
        EmitList(
            w,
            operators.Select(op =>
                $"new {ScriptNames.Api}.ScriptOperatorDescription({ScriptNames.Literal(op.Operator)}, " +
                $"{ScriptNames.Literal(op.Right)}, {ScriptNames.Literal(op.Returns)})"),
            ",");
    }

    private static string PropertyOf(ScriptPropertyModel property)
    {
        string sets = property.Sets.Length > 0 ? $"{ScriptNames.Core}.SettableMemberId.{property.Sets}" : "null";
        string canSet = property.SetBy.Length > 0 ? "CanSet = true, " : "";
        return
            $"new {Property} {{ Name = {ScriptNames.Literal(property.LuauName)}, " +
            $"Type = {ScriptNames.Literal(property.Type.LuauType)}, Summary = {ScriptNames.Literal(property.Summary)}, " +
            $"Sets = {sets}, {canSet}Deprecated = {ScriptNames.Literal(property.Deprecated)} }}";
    }

    private static string FunctionOf(ScriptFunctionModel function)
    {
        IEnumerable<string> signatures = function.Signatures.Select(signature =>
            $"new {ScriptNames.Api}.ScriptSignature({ParametersOf(signature.Parameters)}, {ScriptNames.Literal(signature.Returns)})");

        return
            $"new {Function} {{ Name = {ScriptNames.Literal(function.LuauName)}, " +
            $"Summary = {ScriptNames.Literal(function.Summary)}, Deprecated = {ScriptNames.Literal(function.Deprecated)}, " +
            $"Signatures = [{string.Join(", ", signatures)}] }}";
    }

    private static string ParametersOf(IEnumerable<LuauParameterModel> parameters)
    {
        IEnumerable<string> items = parameters.Select(parameter =>
            $"new {ScriptNames.Api}.ScriptParameter({ScriptNames.Literal(parameter.Name)}, {ScriptNames.Literal(parameter.Type)})");

        return $"[{string.Join(", ", items)}]";
    }

    // A collection expression, one item to a line.
    private static void EmitList(SourceWriter w, IEnumerable<string> items, string trailer)
    {
        w.Line("[");
        w.Indent();
        foreach (string item in items)
            w.Line($"{item},");

        w.Outdent();
        w.Line($"]{trailer}");
    }
}
