using SpectraEngine.Core.Graphics;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Assets;

/// <summary>Type of a scalar/colour/vector parameter declared in a material file.</summary>
public enum MaterialParameterKind
{
    /// <summary>A single scalar.</summary>
    Float,

    /// <summary>Two components.</summary>
    Vector2,

    /// <summary>Three components. Also what a three-component <c>color</c> produces.</summary>
    Vector3,

    /// <summary>Four components. Also what a four-component <c>color</c> produces.</summary>
    Vector4,
}

/// <summary>
/// One parameter parsed from a <c>.spectramat</c> file. The value is always a
/// <see cref="Vector4"/>, narrowed by <see cref="Kind"/>; unused components are zero.
/// </summary>
public readonly record struct MaterialParameter(string Name, MaterialParameterKind Kind, Vector4 Value)
{
    /// <summary>The value as a scalar (component X).</summary>
    public float AsFloat => Value.X;

    /// <summary>The value as a 2-vector (components X, Y).</summary>
    public Vector2 AsVector2 => new(Value.X, Value.Y);

    /// <summary>The value as a 3-vector (components X, Y, Z).</summary>
    public Vector3 AsVector3 => new(Value.X, Value.Y, Value.Z);

    /// <summary>The value as a 4-vector.</summary>
    public Vector4 AsVector4 => Value;
}

/// <summary>One texture binding parsed from a <c>.spectramat</c> file.</summary>
/// <param name="Name">Sampler name in the shader, e.g. <c>uDiffuse</c>.</param>
/// <param name="TexturePath">Content-root-relative path of the image file, as written.</param>
/// <param name="Unit">Texture unit, assigned by declaration order (first slot is 0).</param>
/// <param name="Filter">Defaults to <see cref="TextureFilter.LinearMipmap"/>.</param>
/// <param name="Wrap">Defaults to <see cref="TextureWrap.Repeat"/>.</param>
/// <param name="ColorSpace"><see cref="TextureColorSpace.Srgb"/> unless the line says <c>data</c>.</param>
public readonly record struct MaterialTextureSlot(
    string Name,
    string TexturePath,
    int Unit,
    TextureFilter Filter,
    TextureWrap Wrap,
    TextureColorSpace ColorSpace);

/// <summary>
/// The parsed contents of a <c>.spectramat</c> file. Always usable: lines the
/// parser could not read end up in <see cref="Warnings"/>. Immutable.
/// </summary>
public sealed class MaterialDefinition
{
    internal MaterialDefinition(
        string origin,
        string? shaderName,
        IReadOnlyList<MaterialTextureSlot> textures,
        IReadOnlyList<MaterialParameter> parameters,
        IReadOnlyList<string> warnings)
    {
        Origin = origin;
        ShaderName = shaderName;
        Textures = textures;
        Parameters = parameters;
        Warnings = warnings;
    }

    /// <summary>Where this definition was parsed from. Only labels messages.</summary>
    public string Origin { get; }

    /// <summary>
    /// Shader named by the file's <c>shader</c> key. Null means the built-in lit shader.
    /// </summary>
    public string? ShaderName { get; }

    /// <summary>Texture slots in declaration order; the index is also the texture unit.</summary>
    public IReadOnlyList<MaterialTextureSlot> Textures { get; }

    /// <summary>Scalar/colour/vector parameters in declaration order.</summary>
    public IReadOnlyList<MaterialParameter> Parameters { get; }

    /// <summary>
    /// One message per problem, prefixed with the origin and line number.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Finds a parameter by name (ordinal, case-sensitive).</summary>
    public bool TryGetParameter(string name, out MaterialParameter parameter)
    {
        for (int i = 0; i < Parameters.Count; i++)
        {
            if (Parameters[i].Name == name)
            {
                parameter = Parameters[i];
                return true;
            }
        }

        parameter = default;
        return false;
    }

    /// <summary>Finds a texture slot by sampler name (ordinal, case-sensitive).</summary>
    public bool TryGetTextureSlot(string name, out MaterialTextureSlot slot)
    {
        for (int i = 0; i < Textures.Count; i++)
        {
            if (Textures[i].Name == name)
            {
                slot = Textures[i];
                return true;
            }
        }

        slot = default;
        return false;
    }
}
