using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace SpectraEngine.Entities.Generator;

// The transform stage: one attributed class to an EntityModel. No symbol may
// escape into the model.
internal static class EntityModelFactory
{
    private const string EntityAttribute = "SpectraEngine.Core.Entities.SpectraEntityAttribute";
    private const string KeyvalueAttribute = "SpectraEngine.Core.Entities.KeyvalueAttribute";
    private const string InputAttribute = "SpectraEngine.Core.Entities.EntityInputAttribute";
    private const string OutputAttribute = "SpectraEngine.Core.Entities.EntityOutputAttribute";
    private const string InputContext = "SpectraEngine.Core.Entities.EntityInputContext";

    public const string EntityAttributeMetadataName = EntityAttribute;

    // SceneNode.Name already is the targetname.
    public const string ReservedName = "targetname";

    public static EntityModel? Create(GeneratorAttributeSyntaxContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not INamedTypeSymbol type ||
            context.TargetNode is not ClassDeclarationSyntax declaration)
        {
            return null;
        }

        var diagnostics = new List<DiagnosticInfo>();
        LocationInfo? classLocation = LocationInfo.From(type);

        bool isPartial = declaration.Modifiers.IndexOf(SyntaxKind.PartialKeyword) >= 0;
        var containingTypes = new List<string>();
        for (SyntaxNode? parent = declaration.Parent; parent is TypeDeclarationSyntax outer; parent = parent.Parent)
        {
            isPartial &= outer.Modifiers.IndexOf(SyntaxKind.PartialKeyword) >= 0;
            // Outermost first, so the emitter can open them in order.
            containingTypes.Insert(
                0,
                $"partial {outer.Keyword.ValueText} {outer.Identifier.ValueText}{outer.TypeParameterList}");
        }

        if (!isPartial)
            diagnostics.Add(DiagnosticInfo.Create(EntityDiagnostics.NotPartial, classLocation, type.Name));

        AttributeData attribute = context.Attributes[0];
        string className = FirstStringArgument(attribute);
        string display = NamedString(attribute, "Display");
        string group = NamedString(attribute, "Group");
        string placement = PlacementName(NamedByte(attribute, "Placement", 0));

        var keyvalues = new List<KeyvalueModel>();
        var inputs = new List<InputModel>();
        var outputs = new List<string>();

        foreach (ISymbol member in type.GetMembers())
        {
            token.ThrowIfCancellationRequested();

            if (TryFindAttribute(member, OutputAttribute, out _))
            {
                outputs.Add(member.Name);
            }

            if (TryFindAttribute(member, KeyvalueAttribute, out AttributeData? keyvalue))
                AddKeyvalue(member, keyvalue!, keyvalues, diagnostics);

            if (member is IMethodSymbol method && TryFindAttribute(member, InputAttribute, out AttributeData? input))
                AddInput(method, input!, inputs, diagnostics);
        }

        return new EntityModel(
            Namespace: type.ContainingNamespace.IsGlobalNamespace
                ? ""
                : type.ContainingNamespace.ToDisplayString(),
            ContainingTypes: EquatableArray.From(containingTypes),
            TypeName: $"{declaration.Identifier.ValueText}{declaration.TypeParameterList}",
            FullTypeName: type.ToDisplayString(),
            ClassName: className,
            Display: display.Length > 0 ? display : HumanName(className),
            Group: group,
            Placement: placement,
            IsPartial: isPartial,
            Keyvalues: EquatableArray.From(keyvalues),
            Inputs: EquatableArray.From(inputs),
            Outputs: EquatableArray.From(outputs),
            Diagnostics: EquatableArray.From(diagnostics),
            Location: classLocation);
    }

    private static void AddKeyvalue(
        ISymbol member,
        AttributeData attribute,
        List<KeyvalueModel> keyvalues,
        List<DiagnosticInfo> diagnostics)
    {
        LocationInfo? location = LocationInfo.From(member);
        string name = FirstStringArgument(attribute);

        // Case-insensitive: TargetName would read as the same thing to a person.
        if (string.Equals(name, ReservedName, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(EntityDiagnostics.ReservedKeyvalueName, location, name, member.Name));
            return;
        }

        ITypeSymbol? memberType = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };

        bool assignable = member switch
        {
            IPropertySymbol property => !property.IsStatic && property.SetMethod is { IsInitOnly: false },
            IFieldSymbol field => !field.IsStatic && !field.IsReadOnly && !field.IsConst,
            _ => false,
        };

        if (memberType is null || !assignable)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(EntityDiagnostics.KeyvalueNotAssignable, location, name, member.Name));
            return;
        }

        ClrKind clr = KeyvalueBinding.Classify(memberType);
        KeyvalueRow row;

        if (TryGetNamed(attribute, "Type", out TypedConstant declared))
        {
            byte declaredValue = ToByte(declared);
            bool known = KeyvalueBinding.TryGet(declaredValue, out row);
            if (!known || row.Clr != clr)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    EntityDiagnostics.KeyvalueTypeMismatch,
                    location,
                    name,
                    known ? row.Name : declaredValue.ToString(CultureInfo.InvariantCulture),
                    known ? row.CSharpType : "(a kind this build does not name)",
                    memberType.ToDisplayString()));
                return;
            }
        }
        else if (!KeyvalueBinding.TryInfer(clr, out row))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                EntityDiagnostics.UnsupportedKeyvalueType, location, name, memberType.ToDisplayString()));
            return;
        }

        string display = NamedString(attribute, "Display");
        keyvalues.Add(new KeyvalueModel(
            MemberName: member.Name,
            Name: name,
            Display: display.Length > 0 ? display : HumanName(name),
            Tooltip: NamedString(attribute, "Tooltip"),
            Default: NamedString(attribute, "Default"),
            Type: row.Value,
            Widget: NamedByte(attribute, "Widget", 0),
            Min: NamedFloat(attribute, "Min"),
            Max: NamedFloat(attribute, "Max")));
    }

    private static void AddInput(
        IMethodSymbol method,
        AttributeData attribute,
        List<InputModel> inputs,
        List<DiagnosticInfo> diagnostics)
    {
        // One shape only. A parameterless form would need a second emission path.
        bool valid = method.MethodKind == MethodKind.Ordinary
            && !method.IsStatic
            && !method.IsGenericMethod
            && method.ReturnsVoid
            && method.Parameters.Length == 1
            && method.Parameters[0].RefKind == RefKind.Ref
            && method.Parameters[0].Type.ToDisplayString() == InputContext;

        if (!valid)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                EntityDiagnostics.InvalidInputSignature, LocationInfo.From(method), method.Name));
            return;
        }

        inputs.Add(new InputModel(FirstStringArgument(attribute), method.Name));
    }

    private static bool TryFindAttribute(ISymbol member, string metadataName, out AttributeData? attribute)
    {
        foreach (AttributeData candidate in member.GetAttributes())
        {
            if (candidate.AttributeClass?.ToDisplayString() == metadataName)
            {
                attribute = candidate;
                return true;
            }
        }

        attribute = null;
        return false;
    }

    private static string FirstStringArgument(AttributeData attribute) =>
        attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string value
            ? value
            : "";

    private static bool TryGetNamed(AttributeData attribute, string name, out TypedConstant value)
    {
        foreach (KeyValuePair<string, TypedConstant> pair in attribute.NamedArguments)
        {
            if (pair.Key == name)
            {
                value = pair.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NamedString(AttributeData attribute, string name) =>
        TryGetNamed(attribute, name, out TypedConstant value) && value.Value is string text ? text : "";

    // Absent is not the same as the default: type inference needs to tell them apart.
    private static byte NamedByte(AttributeData attribute, string name, byte fallback) =>
        TryGetNamed(attribute, name, out TypedConstant value) ? ToByte(value) : fallback;

    private static float NamedFloat(AttributeData attribute, string name) =>
        TryGetNamed(attribute, name, out TypedConstant value) && value.Value is float number
            ? number
            : float.NaN;

    private static byte ToByte(TypedConstant value) => value.Value switch
    {
        byte b => b,
        int i => i is >= 0 and <= 255 ? (byte)i : (byte)0,
        _ => 0,
    };

    private static string PlacementName(byte value) => value switch
    {
        1 => "Brush",
        2 => "Abstract",
        _ => "Point",
    };

    // logic_relay becomes "Logic Relay". Splits on underscores only.
    private static string HumanName(string wireName)
    {
        if (wireName.Length == 0)
            return "";

        var text = new StringBuilder(wireName.Length);
        bool startOfWord = true;
        for (int i = 0; i < wireName.Length; i++)
        {
            char c = wireName[i];
            if (c == '_')
            {
                text.Append(' ');
                startOfWord = true;
                continue;
            }

            text.Append(startOfWord ? char.ToUpperInvariant(c) : c);
            startOfWord = false;
        }

        return text.ToString();
    }
}
