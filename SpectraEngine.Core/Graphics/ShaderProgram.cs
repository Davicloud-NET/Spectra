using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Core.Graphics;

public abstract class ShaderProgram : IDisposable
{
    public abstract void Use();

    /// <summary>
    /// Swaps in the compiled code from <paramref name="blob"/> while keeping this
    /// object, so materials holding it keep working. On failure the old code stays.
    /// </summary>
    public abstract bool TryReload(PipelineBlob blob, [NotNullWhen(false)] out string? error);

    public abstract void SetUniform(string name, Matrix4x4 value);
    public abstract void SetUniform(string name, Vector4 value);
    public abstract void SetUniform(string name, Vector3 value);
    public abstract void SetUniform(string name, Vector2 value);
    public abstract void SetUniform(string name, float value);
    public abstract void SetUniform(string name, int value);

    /// <summary>
    /// Fills a <c>vec4[N]</c> uniform array. The length must match the shader's
    /// array.
    /// </summary>
    // Only vec4 and mat4 arrays: HLSL pads every cbuffer array element to 16
    // bytes, so float[] and vec3[] would upload wrong on D3D and fine on GL.
    // A short span is refused because it leaves a stale tail.
    public abstract void SetUniform(string name, ReadOnlySpan<Vector4> values);

    /// <summary>
    /// Fills a <c>mat4[N]</c> uniform array. The length must match the shader's
    /// array.
    /// </summary>
    // No transpose, same as the single-matrix overload: row-major Matrix4x4
    // bytes read correctly as column-major on all three backends.
    public abstract void SetUniform(string name, ReadOnlySpan<Matrix4x4> values);

    /// <summary>Binds <paramref name="texture"/> to a texture unit and assigns it to the named sampler.</summary>
    public abstract void SetTexture(string name, int unit, Texture texture);

    public abstract void Dispose();
}
