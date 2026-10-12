using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Threading;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// The transform stage: one class with a script attribute to a
// ScriptContainerModel. No symbol may escape into the model.
internal static class ScriptModelFactory
{
    private const string ScriptObject = "SpectraEngine.Scripting.Luau.Runtime.ScriptObject";

    private static readonly string[] ContainerAttributes =
    {
        ScriptTypeMap.ClassAttribute, ScriptAttributes.Value, ScriptAttributes.Library, ScriptAttributes.Globals,
    };

    private static readonly HashSet<string> Operators = new()
    {
        "+", "-", "*", "/", "//", "%", "^", "..", "==", "<", "<=", "#", "unm",
    };

    public static ScriptContainerModel? Create(
        GeneratorAttributeSyntaxContext context, ScriptContainerKind kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not ClassDeclarationSyntax declaration)
        {
            return null;
        }

        AttributeData attribute = context.Attributes[0];
        string name = kind == ScriptContainerKind.Globals ? "" : ScriptAttributes.FirstString(attribute);
        var state = new ScriptReadState(kind, type, name, token);
        LocationInfo? location = LocationInfo.From(type);

        List<string> containingTypes = ContainingTypesOf(declaration, out bool isPartial);
        if (!isPartial)
            state.Report(ScriptApiDiagnostics.NotPartial, location, type.Name);

        string summary = kind == ScriptContainerKind.Globals ? "" : state.SummaryOf(type);
        if (kind != ScriptContainerKind.Globals && LuauSignatureText.WhyNotName(name) is { } why)
            state.Report(ScriptApiDiagnostics.NotLuau, ScriptAttributes.LocationOf(attribute), name, type.Name, why);

        bool isPlaced = IsPlaced(state, attribute);
        if (isPlaced)
        {
            foreach (ISymbol member in type.GetMembers())
            {
                token.ThrowIfCancellationRequested();
                ReadMember(state, member);
            }

            ReadOperators(state, type, IsTableType(attribute));
        }

        return new ScriptContainerModel(
            Kind: kind,
            Namespace: type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
            ContainingTypes: EquatableArray.From(containingTypes),
            TypeName: $"{declaration.Identifier.ValueText}{declaration.TypeParameterList}",
            FullTypeName: state.FullTypeName,
            LuauName: name,
            Extends: ScriptAttributes.NamedString(attribute, "Extends"),
            TypeParameters: ScriptAttributes.NamedString(attribute, "TypeParameters"),
            ChildType: ScriptAttributes.NamedString(attribute, "ChildType"),
            Summary: summary,
            IsStatic: type.IsStatic,
            IsPartial: isPartial && isPlaced && type.TypeParameters.Length == 0,
            Self: state.Self,
            Properties: EquatableArray.From(state.Properties),
            Functions: EquatableArray.From(state.Functions),
            Operators: EquatableArray.From(state.Operators),
            Diagnostics: EquatableArray.From(state.Diagnostics),
            Location: location);
    }

    // The types the class is nested in, outermost first, each spelled as the
    // partial declaration the emitter reopens. All of them must be partial.
    private static List<string> ContainingTypesOf(ClassDeclarationSyntax declaration, out bool isPartial)
    {
        isPartial = declaration.Modifiers.IndexOf(SyntaxKind.PartialKeyword) >= 0;
        var containing = new List<string>();
        for (SyntaxNode? parent = declaration.Parent; parent is TypeDeclarationSyntax outer; parent = parent.Parent)
        {
            isPartial &= outer.Modifiers.IndexOf(SyntaxKind.PartialKeyword) >= 0;
            containing.Insert(
                0,
                $"partial {outer.Keyword.ValueText} {outer.Identifier.ValueText}{outer.TypeParameterList}");
        }

        return containing;
    }

    // Whether the attribute can stand on this class at all. When it cannot,
    // nothing is emitted for the class.
    private static bool IsPlaced(ScriptReadState state, AttributeData attribute)
    {
        LocationInfo? at = ScriptAttributes.LocationOf(attribute);
        string? why = null;

        int declared = 0;
        foreach (string container in ContainerAttributes)
            declared += ScriptAttributes.Find(state.Type, container) is null ? 0 : 1;

        if (declared > 1)
            why = "a class declares one of a class, a value, a library and globals";
        else if (state.Type.TypeParameters.Length > 0)
            why = "a generic class cannot be bound";
        else if (state.Kind == ScriptContainerKind.Value && !state.Type.IsStatic)
            why = "a value type is declared on a static class whose members take the value as their first parameter";
        else if (state.Kind == ScriptContainerKind.Class && !state.Type.IsStatic && !DerivesFromScriptObject(state.Type))
            why = "a class that is not static must derive from ScriptObject, whose members the generator overrides";
        else if (IsTableType(attribute) && ScriptAttributes.NamedString(attribute, "Extends").Length > 0)
            why = "a class with type parameters is written as a table type, which extends no class";

        if (why is not null)
            state.Report(ScriptApiDiagnostics.Misplaced, at, state.Type.Name, why);

        return why is null;
    }

    // An extern type takes no type parameters, so the definitions file
    // writes a class with any as a table type.
    private static bool IsTableType(AttributeData attribute) =>
        ScriptAttributes.NamedString(attribute, "TypeParameters").Length > 0;

    private static bool DerivesFromScriptObject(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? @base = type.BaseType; @base is not null; @base = @base.BaseType)
        {
            if (@base.ToDisplayString() == ScriptObject)
                return true;
        }

        return false;
    }

    private static void ReadMember(ScriptReadState state, ISymbol member)
    {
        AttributeData? property = ScriptAttributes.Find(member, ScriptAttributes.Property);
        AttributeData? signal = ScriptAttributes.Find(member, ScriptAttributes.Signal);
        AttributeData? method = ScriptAttributes.Find(member, ScriptAttributes.Method);
        AttributeData? function = ScriptAttributes.Find(member, ScriptAttributes.Function);

        AttributeData? first = property ?? signal ?? method ?? function;
        if (first is null)
            return;

        int declared = (property is null ? 0 : 1) + (signal is null ? 0 : 1) + (method is null ? 0 : 1) + (function is null ? 0 : 1);
        if (declared > 1)
        {
            Misplace(state, member, first, "a member is one of a property, a signal, a method and a function");
        }
        else if (property is not null || signal is not null)
        {
            ScriptPropertyReader.Read(state, member, first, isSignal: signal is not null);
        }
        else if (method is not null && !state.HoldsObjects)
        {
            Misplace(state, member, first, "a library and the globals have functions. Declare it with [ScriptFunction]");
        }
        else if (function is not null && state.HoldsObjects)
        {
            Misplace(state, member, first, "a class and a value have methods. Declare it with [ScriptMethod]");
        }
        else
        {
            ScriptFunctionReader.Read(state, member, first);
        }
    }

    private static void Misplace(ScriptReadState state, ISymbol member, AttributeData attribute, string why) =>
        state.Report(ScriptApiDiagnostics.Misplaced, ScriptAttributes.LocationOf(attribute), member.Name, why);

    // Operators are declared on the class, or on the method that implements one.
    private static void ReadOperators(ScriptReadState state, INamedTypeSymbol type, bool isTableType)
    {
        var declaring = new List<ISymbol> { type };
        declaring.AddRange(type.GetMembers());

        foreach (ISymbol symbol in declaring)
        {
            foreach (AttributeData attribute in ScriptAttributes.All(symbol, ScriptAttributes.Operator))
            {
                string op = ScriptAttributes.StringAt(attribute, 0);
                string right = ScriptAttributes.StringAt(attribute, 1);
                string returns = ScriptAttributes.StringAt(attribute, 2);

                if (!state.HoldsObjects)
                    Misplace(state, symbol, attribute, "an operator is of a class or a value");
                else if (isTableType)
                    Misplace(state, symbol, attribute, "a class with type parameters is written as a table type, which takes no operator");
                else if (!Operators.Contains(op))
                    state.Report(ScriptApiDiagnostics.NotLuau, ScriptAttributes.LocationOf(attribute), op, symbol.Name, "it is no operator of Luau");
                else if (returns.Trim().Length == 0 || (LuauSignatureText.WhyNotBalanced(right) ?? LuauSignatureText.WhyNotBalanced(returns)) is not null)
                    state.Report(ScriptApiDiagnostics.NotLuau, ScriptAttributes.LocationOf(attribute), $"{right} -> {returns}", symbol.Name, "an operator names the type on its right and the type that comes out");
                else
                    state.Operators.Add(new ScriptOperatorModel(op, right.Trim(), returns.Trim()));
            }
        }
    }
}
