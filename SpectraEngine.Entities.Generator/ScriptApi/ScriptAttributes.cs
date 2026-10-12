using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Reads the script attributes off a symbol. They are matched by metadata
// name: this assembly cannot reference the engine.
internal static class ScriptAttributes
{
    private const string Ns = "SpectraEngine.Core.Scripting.Api.";

    public const string Value = Ns + "ScriptValueAttribute";
    public const string Library = Ns + "ScriptLibraryAttribute";
    public const string Globals = Ns + "ScriptGlobalsAttribute";
    public const string Property = Ns + "ScriptPropertyAttribute";
    public const string Method = Ns + "ScriptMethodAttribute";
    public const string Function = Ns + "ScriptFunctionAttribute";
    public const string Signal = Ns + "ScriptSignalAttribute";
    public const string Overload = Ns + "ScriptOverloadAttribute";
    public const string Operator = Ns + "ScriptOperatorAttribute";
    public const string Deprecated = Ns + "ScriptDeprecatedAttribute";

    public static AttributeData? Find(ISymbol symbol, string metadataName)
    {
        foreach (AttributeData candidate in symbol.GetAttributes())
        {
            if (candidate.AttributeClass?.ToDisplayString() == metadataName)
                return candidate;
        }

        return null;
    }

    public static IEnumerable<AttributeData> All(ISymbol symbol, string metadataName)
    {
        foreach (AttributeData candidate in symbol.GetAttributes())
        {
            if (candidate.AttributeClass?.ToDisplayString() == metadataName)
                yield return candidate;
        }
    }

    public static string FirstString(AttributeData attribute) => StringAt(attribute, 0);

    public static string StringAt(AttributeData attribute, int index) =>
        attribute.ConstructorArguments.Length > index && attribute.ConstructorArguments[index].Value is string value
            ? value
            : "";

    public static bool TryGetNamed(AttributeData attribute, string name, out TypedConstant value)
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

    public static string NamedString(AttributeData attribute, string name) =>
        TryGetNamed(attribute, name, out TypedConstant value) && value.Value is string text ? text : "";

    public static bool NamedBool(AttributeData attribute, string name) =>
        TryGetNamed(attribute, name, out TypedConstant value) && value.Value is true;

    // The name of the enum member a named argument was set to, or empty
    // when the argument was not set or names no member.
    public static string NamedEnumMember(AttributeData attribute, string name)
    {
        if (!TryGetNamed(attribute, name, out TypedConstant value) || value.Type is not INamedTypeSymbol type)
            return "";

        foreach (ISymbol member in type.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true } field && Equals(field.ConstantValue, value.Value))
                return field.Name;
        }

        return "";
    }

    public static LocationInfo? LocationOf(AttributeData attribute) =>
        LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax());

    public static string Deprecation(ISymbol member) =>
        Find(member, Deprecated) is { } attribute ? FirstString(attribute) : "";
}
