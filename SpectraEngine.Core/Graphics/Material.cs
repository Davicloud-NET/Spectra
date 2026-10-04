using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Pairs a <see cref="ShaderProgram"/> with the per-surface parameters used to
/// draw it. Usually loaded from a <c>.spectramat</c> file, or built in code
/// with the fluent setters.
/// </summary>
// Apply does not call ShaderProgram.Use. GL binds first and then sets uniforms,
// D3D sets first and Use flushes, so the pipelines own the order.
// Uniform state survives between draws, so a parameter a material leaves out
// keeps the previous draw's value. A material built in code must set every
// parameter its shader reads.
public sealed class Material
{
    private readonly Dictionary<string, float> _floats = new();
    private readonly Dictionary<string, Vector2> _vec2 = new();
    private readonly Dictionary<string, Vector3> _vec3 = new();
    private readonly Dictionary<string, Vector4> _vec4 = new();
    private readonly Dictionary<string, TextureBinding> _textures = new();

    /// <summary>Creates a material drawn with <paramref name="shader"/>. Null means not drawable yet.</summary>
    public Material(ShaderProgram? shader)
    {
        Shader = shader;
    }

    /// <summary>The program this material draws with. Pipelines skip a material with none.</summary>
    public ShaderProgram? Shader { get; internal set; }

    /// <summary>Name for logs and editor UI.</summary>
    public string Name { get; set; } = "unnamed";

    /// <summary>Content-relative path this material was loaded from, or null when built in code.</summary>
    public string? SourcePath { get; internal set; }

    /// <summary>Number of scalar and vector parameters.</summary>
    public int ParameterCount => _floats.Count + _vec2.Count + _vec3.Count + _vec4.Count;

    /// <summary>Number of sampler slots bound.</summary>
    public int TextureCount => _textures.Count;

    public Material SetFloat(string name, float value) { _floats[name] = value; return this; }
    public Material SetVector2(string name, Vector2 value) { _vec2[name] = value; return this; }
    public Material SetVector3(string name, Vector3 value) { _vec3[name] = value; return this; }
    public Material SetVector4(string name, Vector4 value) { _vec4[name] = value; return this; }

    /// <summary>
    /// Binds this exact texture to the named sampler. Use the
    /// <see cref="Assets.TextureAsset"/> overload for anything the asset manager may swap.
    /// </summary>
    public Material SetTexture(string name, int unit, Texture texture)
    {
        _textures[name] = new TextureBinding(unit, texture, null);
        return this;
    }

    /// <summary>
    /// Binds an asset handle to the named sampler. Resolved at <see cref="Apply"/>
    /// time, so the material follows async loads and hot reloads.
    /// </summary>
    public Material SetTexture(string name, int unit, Assets.TextureAsset asset)
    {
        _textures[name] = new TextureBinding(unit, null, asset);
        return this;
    }

    /// <summary>
    /// How many sampler slots are bound directly to <paramref name="texture"/>.
    /// Slots bound through an asset handle are not counted.
    /// </summary>
    public int CountBindingsTo(Texture? texture)
    {
        if (texture is null) return 0;

        int count = 0;
        foreach (TextureBinding binding in _textures.Values)
        {
            if (ReferenceEquals(binding.Direct, texture)) count++;
        }

        return count;
    }

    public bool TryGetFloat(string name, out float value) => _floats.TryGetValue(name, out value);

    public bool TryGetVector2(string name, out Vector2 value) => _vec2.TryGetValue(name, out value);

    public bool TryGetVector3(string name, out Vector3 value) => _vec3.TryGetValue(name, out value);

    public bool TryGetVector4(string name, out Vector4 value) => _vec4.TryGetValue(name, out value);

    /// <summary>
    /// Reads back a sampler binding: its unit and the texture it currently
    /// resolves to. Render thread only.
    /// </summary>
    public bool TryGetTexture(string name, out int unit, [MaybeNullWhen(false)] out Texture texture)
    {
        if (_textures.TryGetValue(name, out TextureBinding binding))
        {
            unit = binding.Unit;
            texture = binding.Resolve();
            return true;
        }

        unit = 0;
        texture = null;
        return false;
    }

    // Called before the textures behind the bindings are destroyed.
    internal void ClearTextures() => _textures.Clear();

    /// <summary>Uploads this material's parameters to its own shader. No-op without one.</summary>
    public void Apply()
    {
        if (Shader is not { } shader) return;
        ApplyTo(shader);
    }

    /// <summary>
    /// Uploads this material's parameters to <paramref name="shader"/> instead of
    /// its own, for a pass that picks the program (the deferred geometry pass).
    /// Names the shader does not declare are ignored.
    /// </summary>
    public void ApplyTo(ShaderProgram shader)
    {
        foreach (var (name, value) in _floats) shader.SetUniform(name, value);
        foreach (var (name, value) in _vec2) shader.SetUniform(name, value);
        foreach (var (name, value) in _vec3) shader.SetUniform(name, value);
        foreach (var (name, value) in _vec4) shader.SetUniform(name, value);
        foreach (var (name, binding) in _textures) shader.SetTexture(name, binding.Unit, binding.Resolve());
    }

    // One of Direct/Asset is set.
    private readonly record struct TextureBinding(int Unit, Texture? Direct, Assets.TextureAsset? Asset)
    {
        public Texture Resolve() => Asset is not null ? Asset.Texture : Direct!;
    }
}
