using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Reads [ScriptProperty] and [ScriptSignal] off a member: a property of a
// class or a value, a constant of a library, a global value or a signal.
internal static class ScriptPropertyReader
{
    public static void Read(ScriptReadState state, ISymbol member, AttributeData attribute, bool isSignal)
    {
        LocationInfo? at = ScriptAttributes.LocationOf(attribute);
        bool isByHand = ScriptAttributes.NamedBool(attribute, "ByHand");
        if (WhyMisplaced(state, isSignal, isByHand) is { } misplaced)
        {
            state.Report(ScriptApiDiagnostics.Misplaced, at, member.Name, misplaced);
            return;
        }

        if (state.Claim(member, attribute) is not { } name)
            return;

        string summary = state.SummaryOf(member);
        if (ReadAccess(state, member, isByHand) is not { } access)
            return;

        string luauOverride = ScriptAttributes.NamedString(attribute, "Type");
        ScriptTypeModel? type = TypeOf(state, member, access.Type, luauOverride, isByHand);
        if (type is null || !IsKindAllowed(state, member, type, isSignal, isByHand))
            return;

        var signalParameters = new List<LuauParameterModel>();
        if (isSignal && LuauSignatureText.ReadParameters(ScriptAttributes.FirstString(attribute), signalParameters) is { } wrong)
        {
            state.Report(ScriptApiDiagnostics.NotLuau, at, ScriptAttributes.FirstString(attribute), member.Name, wrong);
            return;
        }

        string sets = "";
        if (!isSignal && ScriptAttributes.TryGetNamed(attribute, "Sets", out _))
        {
            sets = ScriptAttributes.NamedEnumMember(attribute, "Sets");
            if (WhyNotSettable(state, member, type, sets) is { } why)
            {
                state.Report(ScriptApiDiagnostics.NotSettable, at, name, why);
                return;
            }
        }

        state.Properties.Add(new ScriptPropertyModel(
            LuauName: name,
            Summary: summary,
            Type: type,
            ReadFormat: access.Format,
            MemberName: member.Name,
            Access: SyntaxFacts.GetText(member.DeclaredAccessibility),
            SelfName: access.SelfName,
            Sets: sets,
            CannotSet: isSignal ? "" : ScriptAttributes.NamedString(attribute, "CannotSet"),
            IsSignal: isSignal,
            SignalParameters: EquatableArray.From(signalParameters),
            IsByHand: isByHand,
            Deprecated: ScriptAttributes.Deprecation(member)));
    }

    private static string? WhyMisplaced(ScriptReadState state, bool isSignal, bool isByHand)
    {
        if (isSignal && state.Kind != ScriptContainerKind.Class)
            return "a signal is a member of a class";
        if (isByHand && state.HoldsObjects)
            return "only a constant of a library and a global can be bound by hand";

        return null;
    }

    // How the generated code reads the member, and the type it reads. Null
    // when the member has the wrong shape, which is reported.
    private static (string Format, ITypeSymbol Type, string SelfName)? ReadAccess(
        ScriptReadState state, ISymbol member, bool isByHand)
    {
        if (state.TakesObjectFirst)
        {
            if (member is not IMethodSymbol { IsStatic: true, IsGenericMethod: false, ReturnsVoid: false, Parameters.Length: 1 } getter)
            {
                state.Report(
                    ScriptApiDiagnostics.NoObjectParameter, LocationInfo.From(member), member.Name,
                    state.Self?.LuauType ?? "the object", state.Described);
                return null;
            }

            if (!state.IsSelf(getter.Parameters[0].Type, member))
                return null;

            return ($"{state.FullTypeName}.{member.Name}({{0}})", getter.ReturnType, getter.Parameters[0].Name);
        }

        string owner = member.IsStatic ? state.FullTypeName : state.HoldsObjects ? "{0}" : "this";
        switch (member)
        {
            case IPropertySymbol { GetMethod: not null, IsIndexer: false } property:
                return ($"{owner}.{member.Name}", property.Type, "");

            case IMethodSymbol { IsGenericMethod: false, ReturnsVoid: false } method when isByHand || method.Parameters.Length == 0:
                return ($"{owner}.{member.Name}()", method.ReturnType, "");

            default:
                state.Report(
                    ScriptApiDiagnostics.WrongShape, LocationInfo.From(member), member.Name,
                    "it is declared on a property that can be read, or on a method that takes nothing and returns it");
                return null;
        }
    }

    private static ScriptTypeModel? TypeOf(
        ScriptReadState state, ISymbol member, ITypeSymbol clr, string luauOverride, bool isByHand)
    {
        ScriptTypeModel? type = ScriptTypeMap.Of(clr);

        // What is bound by hand needs no C# type the generator knows.
        if (type is null && isByHand && luauOverride.Length > 0)
            return new ScriptTypeModel(ScriptValueKind.Object, "object", IsNullable: false, luauOverride);

        if (type is null)
        {
            state.Report(
                ScriptApiDiagnostics.TypeNotCarried, LocationInfo.From(member), member.Name, clr.ToDisplayString());
            return null;
        }

        return luauOverride.Length > 0 ? type with { LuauType = luauOverride } : type;
    }

    private static bool IsKindAllowed(
        ScriptReadState state, ISymbol member, ScriptTypeModel type, bool isSignal, bool isByHand)
    {
        string? why = null;
        if (type.Kind == ScriptValueKind.Utf8)
            why = "a ReadOnlySpan<byte> is only a way to take a string, and cannot be handed to a script";
        else if (isSignal && (type.Kind != ScriptValueKind.Object || type.IsNullable))
            why = "a signal is declared on a property that gives the signal object";
        else if (isByHand)
            why = null;
        else if (state.Kind == ScriptContainerKind.Library && type.Kind == ScriptValueKind.Object)
            why = "a constant of a library is a number, a boolean, a string, a Vector3 or a CFrame";
        else if (state.Kind == ScriptContainerKind.Globals && type.Kind != ScriptValueKind.Object)
            why = "a global value is an object. A constant belongs in a library";

        if (why is not null)
            state.Report(ScriptApiDiagnostics.WrongShape, LocationInfo.From(member), member.Name, why);

        return why is null;
    }

    // The generator writes the read of a property a script can set, from
    // the door, so what is read and what is written are one member of it.
    private static string? WhyNotSettable(ScriptReadState state, ISymbol member, ScriptTypeModel type, string sets)
    {
        if (sets.Length == 0)
            return "Sets names no member of SettableMemberId";
        if (state.Kind != ScriptContainerKind.Class || state.Self?.ClrType != ScriptTypeMap.SceneNode)
            return "the door only sets a member of a node, so the class must be one whose members take a SceneNode";
        if (type.IsNullable || type.Kind is not (ScriptValueKind.Text or ScriptValueKind.Vector3 or ScriptValueKind.CFrame))
            return "the door takes a string, a Vector3 or a CFrame";
        if (member is not IMethodSymbol { IsPartialDefinition: true, PartialImplementationPart: null })
            return "declare it as a partial method with no body. The generator writes the body, which reads the member from the door";

        return null;
    }
}
