using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler.Syntax;

namespace SpectraShade.Compiler.Analysis;

/// <summary>
/// Resolves a shader's vertex input struct into the locations and rates the
/// renderer has to build an input layout from.
/// </summary>
// Shared by the analyzer and both generators so declared and reported layouts agree.
public static class VertexInputLayout
{
    /// <summary>Marks a vertex input as advancing once per instance.</summary>
    public const string PerInstanceAttribute = "PerInstance";

    /// <summary>
    /// Floats per location and locations occupied for a vertex input type.
    /// False if the type cannot be a vertex input. A matrix spans several locations.
    /// </summary>
    public static bool TryDescribeType(string typeName, out uint componentCount, out uint locationSpan)
    {
        (componentCount, locationSpan) = typeName switch
        {
            "float" or "int" or "uint" or "bool" => (1u, 1u),
            "vec2" or "ivec2" or "uvec2" or "bvec2" => (2u, 1u),
            "vec3" or "ivec3" or "uvec3" or "bvec3" => (3u, 1u),
            "vec4" or "ivec4" or "uvec4" or "bvec4" => (4u, 1u),
            "mat2" => (2u, 2u),
            "mat3" => (3u, 3u),
            "mat4" => (4u, 4u),
            _ => (0u, 0u),
        };

        return locationSpan > 0;
    }

    /// <summary>Whether <paramref name="field"/> is declared per instance.</summary>
    public static bool IsPerInstance(FieldDeclaration field) =>
        field.Attributes.Any(a => string.Equals(a.Name, PerInstanceAttribute, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The location <paramref name="field"/> starts at: its <c>[Location(N)]</c>
    /// if it has a literal one, else <paramref name="fieldIndex"/>.
    /// </summary>
    // Existing shaders rely on the index fallback. It overlaps once a matrix is
    // involved, so the analyzer refuses that combination.
    public static uint ResolveLocation(FieldDeclaration field, int fieldIndex)
    {
        AttributeSyntax? location = field.Attributes.FirstOrDefault(a =>
            string.Equals(a.Name, "Location", StringComparison.OrdinalIgnoreCase));

        if (location is not null && location.Arguments.Count > 0
            && location.Arguments[0] is IntLiteralExpression literal && literal.Value >= 0)
        {
            return (uint)literal.Value;
        }

        return (uint)fieldIndex;
    }

    /// <summary>Whether <paramref name="field"/> carries a literal <c>[Location(N)]</c>.</summary>
    public static bool HasExplicitLocation(FieldDeclaration field) =>
        field.Attributes.Any(a =>
            string.Equals(a.Name, "Location", StringComparison.OrdinalIgnoreCase)
            && a.Arguments.Count > 0 && a.Arguments[0] is IntLiteralExpression { Value: >= 0 });

    /// <summary>
    /// Describes the inputs of <paramref name="vertexFunc"/>'s first parameter struct.
    /// </summary>
    public static VertexInputElement[] DescribeFor(
        FunctionDeclaration? vertexFunc, IReadOnlyList<StructDeclaration> allStructs)
    {
        if (vertexFunc is null || vertexFunc.Parameters.Count == 0)
            return [];

        string name = vertexFunc.Parameters[0].Type.Name;
        return Describe(allStructs.FirstOrDefault(s => s.Name == name));
    }

    /// <summary>
    /// Describes the fields of <paramref name="inputStruct"/> in declaration order.
    /// Fields that cannot be vertex inputs are skipped; the analyzer reports them.
    /// </summary>
    public static VertexInputElement[] Describe(StructDeclaration? inputStruct)
    {
        if (inputStruct is null)
            return [];

        var elements = new List<VertexInputElement>(inputStruct.Fields.Count);
        for (int i = 0; i < inputStruct.Fields.Count; i++)
        {
            FieldDeclaration field = inputStruct.Fields[i];
            if (!TryDescribeType(field.Type.Name, out uint components, out uint span))
                continue;

            elements.Add(new VertexInputElement(
                field.Name,
                ResolveLocation(field, i),
                span,
                components,
                IsPerInstance(field) ? VertexInputRate.PerInstance : VertexInputRate.PerVertex));
        }

        return [.. elements];
    }
}
