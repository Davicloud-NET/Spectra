using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Reads [ScriptMethod] and [ScriptFunction] off a method. One whose
// parameters are C# values gets its arguments checked by generated code.
// One that takes a LuauStack is written by hand and only called.
internal static class ScriptFunctionReader
{
    public static void Read(ScriptReadState state, ISymbol member, AttributeData attribute)
    {
        // Only [ScriptFunction] has ByHand, so a method of a class never is.
        bool isByHand = ScriptAttributes.NamedBool(attribute, "ByHand");
        if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary, IsGenericMethod: false } method)
        {
            state.Report(
                ScriptApiDiagnostics.WrongShape, LocationInfo.From(member), member.Name,
                "it must be an ordinary method that is not generic");
            return;
        }

        if (state.Claim(member, attribute) is not { } name)
            return;

        string summary = state.SummaryOf(member);
        int contexts = ContextCountOf(method);
        bool isByStack = method.Parameters.Length > contexts && IsStack(method.Parameters[contexts].Type);
        ScriptFunctionModel? function = isByHand || isByStack
            ? ReadWritten(state, method, attribute, name, summary, isByHand)
            : ReadTyped(state, method, attribute, name, summary);

        if (function is not null)
            state.Functions.Add(function);
    }

    // A function whose binding is written by hand. The attribute says what
    // a script may hand it.
    private static ScriptFunctionModel? ReadWritten(
        ScriptReadState state, IMethodSymbol method, AttributeData attribute, string name, string summary, bool isByHand)
    {
        string call = "";
        if (!isByHand && (call = StackCallOf(state, method)).Length == 0)
            return null;

        var signatures = new List<ScriptSignatureModel>();
        foreach (AttributeData overload in ScriptAttributes.All(method, ScriptAttributes.Overload))
        {
            if (ReadSignature(state, method, overload, ScriptAttributes.StringAt(overload, 0), ScriptAttributes.StringAt(overload, 1)) is not { } shape)
                return null;

            signatures.Add(shape);
        }

        if (signatures.Count == 0)
        {
            string parameters = ScriptAttributes.NamedString(attribute, "Parameters");
            string returns = ScriptAttributes.NamedString(attribute, "Returns");
            if (ReadSignature(state, method, attribute, parameters, returns) is not { } shape)
                return null;

            signatures.Add(shape);
        }

        return new ScriptFunctionModel(
            name, summary, call, method.IsStatic, IsByStack: !isByHand, TakesSelf: state.TakesObjectFirst, isByHand,
            EquatableArray<ScriptParameterModel>.Empty, EquatableArray<ScriptTypeModel>.Empty,
            EquatableArray.From(signatures), ScriptAttributes.Deprecation(method));
    }

    // The call of a method that takes the stack, with {0} for the object.
    // Empty when the method has another shape than its place asks for. The
    // context may come in front of the stack: every place a call is written
    // has one.
    private static string StackCallOf(ScriptReadState state, IMethodSymbol method)
    {
        bool returnsCount = method.ReturnType.SpecialType == SpecialType.System_Int32;
        int contexts = ContextCountOf(method);
        string handed = contexts == 0 ? "stack" : "context, stack";
        if (state.TakesObjectFirst)
        {
            if (!returnsCount || !method.IsStatic || method.Parameters.Length != contexts + 2)
                return Refuse(state, method, "written by hand, it is static, takes a LuauStack and then the object, and returns an int");

            return state.IsSelf(method.Parameters[contexts + 1].Type, method)
                ? $"{state.FullTypeName}.{method.Name}({handed}, {{0}})"
                : "";
        }

        if (!returnsCount || method.Parameters.Length != contexts + 1)
            return Refuse(state, method, "written by hand, it takes a LuauStack and nothing else, and returns an int");

        string owner = method.IsStatic ? state.FullTypeName : state.HoldsObjects ? "{0}" : "this";
        return $"{owner}.{method.Name}({handed})";
    }

    // One when the method takes the ScriptContext first, as one written by
    // hand may, to reach what the level keeps for its scripts.
    internal static int ContextCountOf(IMethodSymbol method) =>
        method.Parameters.Length > 0
        && method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == ScriptNames.Context
            ? 1
            : 0;

    private static ScriptSignatureModel? ReadSignature(
        ScriptReadState state, IMethodSymbol method, AttributeData attribute, string parameters, string returns)
    {
        var read = new List<LuauParameterModel>();
        string? wrong = LuauSignatureText.ReadParameters(parameters, read);
        string text = parameters;
        if (wrong is null && (wrong = LuauSignatureText.WhyNotBalanced(returns)) is not null)
            text = returns;

        if (wrong is null)
            return new ScriptSignatureModel(EquatableArray.From(read), returns.Trim());

        state.Report(ScriptApiDiagnostics.NotLuau, ScriptAttributes.LocationOf(attribute), text, method.Name, wrong);
        return null;
    }

    private static ScriptFunctionModel? ReadTyped(
        ScriptReadState state, IMethodSymbol method, AttributeData attribute, string name, string summary)
    {
        if (ScriptAttributes.TryGetNamed(attribute, "Parameters", out _)
            || ScriptAttributes.TryGetNamed(attribute, "Returns", out _)
            || ScriptAttributes.Find(method, ScriptAttributes.Overload) is not null)
        {
            Refuse(state, method, "its parameters are its own. Only a method that takes a LuauStack says them in the attribute");
            return null;
        }

        if (state.TakesObjectFirst && (!method.IsStatic || method.Parameters.Length == 0))
        {
            state.Report(
                ScriptApiDiagnostics.NoObjectParameter, LocationInfo.From(method), method.Name,
                state.Self?.LuauType ?? "the object", state.Described);
            return null;
        }

        if (state.TakesObjectFirst && !state.IsSelf(method.Parameters[0].Type, method))
            return null;

        var parameters = new List<ScriptParameterModel>();
        foreach (IParameterSymbol parameter in method.Parameters.Skip(state.TakesObjectFirst ? 1 : 0))
        {
            if (parameter.RefKind != RefKind.None || ScriptTypeMap.Of(parameter.Type) is not { } type)
                return NotCarried(state, method, parameter.Type);

            // The description names the parameter as C# does.
            if (LuauSignatureText.WhyNotName(parameter.Name) is { } why)
            {
                state.Report(ScriptApiDiagnostics.NotLuau, LocationInfo.From(parameter), parameter.Name, method.Name, why);
                return null;
            }

            bool isOptional = type.IsNullable || parameter.HasExplicitDefaultValue;
            parameters.Add(new ScriptParameterModel(parameter.Name, type, isOptional, DefaultOf(parameter, type)));
        }

        var returns = new List<ScriptTypeModel>();
        foreach (ITypeSymbol returned in ReturnedBy(method))
        {
            if (ScriptTypeMap.Of(returned) is not { Kind: not ScriptValueKind.Utf8 } type)
                return NotCarried(state, method, returned);

            returns.Add(type);
        }

        string owner = method.IsStatic ? state.FullTypeName : state.HoldsObjects ? "{0}" : "this";
        return new ScriptFunctionModel(
            name, summary, $"{owner}.{method.Name}(", method.IsStatic, IsByStack: false, TakesSelf: state.TakesObjectFirst, IsByHand: false,
            EquatableArray.From(parameters), EquatableArray.From(returns),
            new EquatableArray<ScriptSignatureModel>(new[] { SignatureOf(parameters, returns) }),
            ScriptAttributes.Deprecation(method));
    }

    // Nothing for void, each element of a tuple, or the one type.
    private static IEnumerable<ITypeSymbol> ReturnedBy(IMethodSymbol method)
    {
        if (method.ReturnsVoid)
            return Enumerable.Empty<ITypeSymbol>();

        return method.ReturnType is INamedTypeSymbol { IsTupleType: true } tuple
            ? tuple.TupleElements.Select(element => element.Type)
            : new[] { method.ReturnType };
    }

    private static ScriptSignatureModel SignatureOf(List<ScriptParameterModel> parameters, List<ScriptTypeModel> returns)
    {
        var described = new List<LuauParameterModel>();
        foreach (ScriptParameterModel parameter in parameters)
        {
            string type = parameter.Type.LuauType;
            described.Add(new LuauParameterModel(parameter.Name, parameter.IsOptional && !type.EndsWith("?", System.StringComparison.Ordinal) ? type + "?" : type));
        }

        string joined = string.Join(", ", returns.Select(type => type.LuauType));
        return new ScriptSignatureModel(EquatableArray.From(described), returns.Count > 1 ? $"({joined})" : joined);
    }

    // The value a parameter has when a script leaves it out, as a C# literal.
    // Empty for one a script must hand over.
    private static string DefaultOf(IParameterSymbol parameter, ScriptTypeModel type)
    {
        if (!parameter.HasExplicitDefaultValue)
            return type.IsNullable ? "null" : "";

        return parameter.ExplicitDefaultValue switch
        {
            bool flag => flag ? "true" : "false",
            float number => WrittenNumber(parameter) ?? $"global::System.BitConverter.Int32BitsToSingle({BitsOf(number)})",
            double number => WrittenNumber(parameter) ?? $"global::System.BitConverter.Int64BitsToDouble({BitsOf(number)}L)",
            int number => number.ToString(CultureInfo.InvariantCulture),
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            _ => type.IsNullable || type.Kind is ScriptValueKind.Text or ScriptValueKind.Object ? "null" : "default",
        };
    }

    // The default as its author wrote it, when that is a number and no
    // name. A number formatted here reads differently on another host of
    // the compiler, and NaN and infinity format as no C# at all.
    private static string? WrittenNumber(IParameterSymbol parameter)
    {
        foreach (SyntaxReference reference in parameter.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not ParameterSyntax { Default.Value: { } value })
                continue;

            bool isNegated = value.IsKind(SyntaxKind.UnaryMinusExpression);
            ExpressionSyntax number = isNegated ? ((PrefixUnaryExpressionSyntax)value).Operand : value;
            if (number.IsKind(SyntaxKind.NumericLiteralExpression))
                return value.ToString();
        }

        return null;
    }

    private static string BitsOf(float number) =>
        BitConverter.ToInt32(BitConverter.GetBytes(number), 0).ToString(CultureInfo.InvariantCulture);

    private static string BitsOf(double number) =>
        BitConverter.DoubleToInt64Bits(number).ToString(CultureInfo.InvariantCulture);

    private static bool IsStack(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == ScriptTypeMap.LuauStack;

    private static string Refuse(ScriptReadState state, IMethodSymbol method, string why)
    {
        state.Report(ScriptApiDiagnostics.WrongShape, LocationInfo.From(method), method.Name, why);
        return "";
    }

    private static ScriptFunctionModel? NotCarried(ScriptReadState state, IMethodSymbol method, ITypeSymbol type)
    {
        state.Report(ScriptApiDiagnostics.TypeNotCarried, LocationInfo.From(method), method.Name, type.ToDisplayString());
        return null;
    }
}
