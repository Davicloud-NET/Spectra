using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// A full-screen shader invocation: the program plus the values to give it.
/// Build one once and refill it each frame.
/// </summary>
// Values are staged, not set: GL needs Use() before uniform writes, both D3D
// backends need it after. Each backend replays them at its own legal point.
// Use(); set; Use(); is not a way out: D3D12's first Use() clears the pending
// texture table and the pass samples white.
public sealed class PostPass
{
    private readonly Dictionary<string, float> _floats = [];
    private readonly Dictionary<string, int> _ints = [];
    private readonly Dictionary<string, Vector2> _vec2 = [];
    private readonly Dictionary<string, Vector3> _vec3 = [];
    private readonly Dictionary<string, Vector4> _vec4 = [];
    private readonly Dictionary<string, Matrix4x4> _matrices = [];
    private readonly Dictionary<string, Vector4[]> _vec4Arrays = [];
    private readonly Dictionary<string, Matrix4x4[]> _matrixArrays = [];
    private readonly Dictionary<string, (int Unit, Texture Texture)> _textures = [];

    public PostPass(ShaderProgram shader)
    {
        Shader = shader;
    }

    /// <summary>The program this pass runs.</summary>
    public ShaderProgram Shader { get; }

    /// <summary>Stages a scalar uniform.</summary>
    public PostPass SetUniform(string name, float value)
    {
        _floats[name] = value;
        return this;
    }

    /// <summary>Stages an integer uniform.</summary>
    public PostPass SetUniform(string name, int value)
    {
        _ints[name] = value;
        return this;
    }

    /// <summary>Stages a 2-vector uniform.</summary>
    public PostPass SetUniform(string name, Vector2 value)
    {
        _vec2[name] = value;
        return this;
    }

    /// <summary>Stages a 3-vector uniform.</summary>
    public PostPass SetUniform(string name, Vector3 value)
    {
        _vec3[name] = value;
        return this;
    }

    /// <summary>Stages a 4-vector uniform.</summary>
    public PostPass SetUniform(string name, Vector4 value)
    {
        _vec4[name] = value;
        return this;
    }

    /// <summary>Stages a matrix uniform.</summary>
    public PostPass SetUniform(string name, Matrix4x4 value)
    {
        _matrices[name] = value;
        return this;
    }

    /// <summary>
    /// Stages a <c>vec4</c> array uniform. The values are copied, so the
    /// caller's span can be reused straight away.
    /// </summary>
    public PostPass SetUniform(string name, ReadOnlySpan<Vector4> values)
    {
        if (!_vec4Arrays.TryGetValue(name, out Vector4[]? buffer) || buffer.Length != values.Length)
            _vec4Arrays[name] = buffer = new Vector4[values.Length];

        values.CopyTo(buffer);
        return this;
    }

    /// <summary>Stages a <c>mat4</c> array uniform. The values are copied.</summary>
    public PostPass SetUniform(string name, ReadOnlySpan<Matrix4x4> values)
    {
        if (!_matrixArrays.TryGetValue(name, out Matrix4x4[]? buffer) || buffer.Length != values.Length)
            _matrixArrays[name] = buffer = new Matrix4x4[values.Length];

        values.CopyTo(buffer);
        return this;
    }

    /// <summary>Stages a texture on a sampler unit.</summary>
    public PostPass SetTexture(string name, int unit, Texture texture)
    {
        _textures[name] = (unit, texture);
        return this;
    }

    // Each backend calls this where its own Use() order allows uniform writes.
    internal void ApplyTo(ShaderProgram shader)
    {
        foreach ((string name, float value) in _floats)
            shader.SetUniform(name, value);
        foreach ((string name, int value) in _ints)
            shader.SetUniform(name, value);
        foreach ((string name, Vector2 value) in _vec2)
            shader.SetUniform(name, value);
        foreach ((string name, Vector3 value) in _vec3)
            shader.SetUniform(name, value);
        foreach ((string name, Vector4 value) in _vec4)
            shader.SetUniform(name, value);
        foreach ((string name, Matrix4x4 value) in _matrices)
            shader.SetUniform(name, value);
        foreach ((string name, Vector4[] values) in _vec4Arrays)
            shader.SetUniform(name, values);
        foreach ((string name, Matrix4x4[] values) in _matrixArrays)
            shader.SetUniform(name, values);
        foreach ((string name, (int unit, Texture texture)) in _textures)
            shader.SetTexture(name, unit, texture);
    }
}
