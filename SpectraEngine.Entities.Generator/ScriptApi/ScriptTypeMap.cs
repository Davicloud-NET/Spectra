using Microsoft.CodeAnalysis;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Which C# types cross into Luau, and what Luau calls each. A new kind of
// value is added here, in ScriptMarshalling and in the binding's readers.
internal static class ScriptTypeMap
{
    public const string ClassAttribute = "SpectraEngine.Core.Scripting.Api.ScriptClassAttribute";
    public const string SceneNode = "global::SpectraEngine.Core.Scene.SceneNode";
    public const string LuauStack = "global::SpectraEngine.Scripting.Luau.LuauStack";

    private const string Vector3 = "global::System.Numerics.Vector3";
    private const string CFrame = "global::SpectraEngine.Core.Scene.CFrame";
    private const string Utf8 = "global::System.ReadOnlySpan<byte>";

    // Null for a type no script value is made from.
    public static ScriptTypeModel? Of(ITypeSymbol type)
    {
        bool isNullable = type.NullableAnnotation == NullableAnnotation.Annotated;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapped)
        {
            type = wrapped.TypeArguments[0];
            isNullable = true;
        }

        string clr = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        (ScriptValueKind Kind, string Luau)? found = type.SpecialType switch
        {
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Int32 =>
                (ScriptValueKind.Number, "number"),
            SpecialType.System_Boolean => (ScriptValueKind.Boolean, "boolean"),
            SpecialType.System_String => (ScriptValueKind.Text, "string"),
            _ => clr switch
            {
                Vector3 => (ScriptValueKind.Vector3, "Vector3"),
                CFrame => (ScriptValueKind.CFrame, "CFrame"),
                Utf8 => (ScriptValueKind.Utf8, "string"),
                SceneNode => (ScriptValueKind.Object, "Instance"),
                _ => ClassNameOf(type) is { } name ? (ScriptValueKind.Object, name) : null,
            },
        };

        return found is { } known
            ? new ScriptTypeModel(known.Kind, clr, isNullable, isNullable ? known.Luau + "?" : known.Luau)
            : null;
    }

    // The name [ScriptClass] gives a class, or null when it has none.
    public static string? ClassNameOf(ITypeSymbol type)
    {
        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ClassAttribute)
                return ScriptAttributes.FirstString(attribute);
        }

        return null;
    }
}
