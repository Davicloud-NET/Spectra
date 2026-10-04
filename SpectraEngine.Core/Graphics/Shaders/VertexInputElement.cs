namespace SpectraEngine.Core.Graphics.Shaders;

/// <summary>
/// How often a vertex input advances. Shader text cannot say this on either
/// backend, so the renderer has to set it on the layout.
/// </summary>
public enum VertexInputRate
{
    PerVertex,

    /// <summary>Advances once per instance, with a step rate of 1.</summary>
    PerInstance,
}

/// <summary>One vertex input a compiled shader declares.</summary>
/// <param name="Name">The field's name in the shader's vertex input struct.</param>
/// <param name="Location">
/// The first location it occupies: the GLSL <c>layout(location = N)</c> and
/// the HLSL <c>TEXCOORD</c> semantic index.
/// </param>
/// <param name="LocationSpan">
/// How many consecutive locations it occupies: 1 for scalars and vectors, 4 for
/// a <c>mat4</c>, 3 for a <c>mat3</c>, 2 for a <c>mat2</c>.
/// </param>
/// <param name="ComponentCount">
/// Floats per location. A <c>mat4</c> reports 4, not 16.
/// </param>
/// <param name="Rate">Per vertex or per instance.</param>
public readonly record struct VertexInputElement(
    string Name,
    uint Location,
    uint LocationSpan,
    uint ComponentCount,
    VertexInputRate Rate)
{
    /// <summary>The location just past this element.</summary>
    public uint LocationEnd => Location + LocationSpan;

    /// <summary>Whether this element and <paramref name="other"/> claim any location in common.</summary>
    public bool Overlaps(in VertexInputElement other) =>
        Location < other.LocationEnd && other.Location < LocationEnd;
}
