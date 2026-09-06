using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
namespace SpectraEngine.Bsp.Tests;
internal sealed class NoopShaderProgram : ShaderProgram
{
    public override void Use() { }
    public override bool TryReload(PipelineBlob blob, [NotNullWhen(false)] out string? error) { error = "probe"; return false; }
    public override void SetUniform(string name, Matrix4x4 value) { }
    public override void SetUniform(string name, Vector4 value) { }
    public override void SetUniform(string name, Vector3 value) { }
    public override void SetUniform(string name, Vector2 value) { }
    public override void SetUniform(string name, float value) { }
    public override void SetUniform(string name, int value) { }
    public override void SetUniform(string name, ReadOnlySpan<Vector4> value) { }
    public override void SetUniform(string name, ReadOnlySpan<Matrix4x4> value) { }
    public override void SetTexture(string name, int unit, Texture texture) { }
    public override void Dispose() { }
}
