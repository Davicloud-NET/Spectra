using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// Shared fixtures for the scene spatial-index tests.
internal static class SpatialTestHelpers
{
    public static readonly Material NoopMaterial = new(new NoopShaderProgram());

    public static TestMesh CreateCubeMesh(float half)
    {
        Vector3[] positions =
        [
            new(-half, -half, -half), new(half, -half, -half),
            new(-half, half, -half), new(half, half, -half),
            new(-half, -half, half), new(half, -half, half),
            new(-half, half, half), new(half, half, half),
        ];
        // Winding doesn't matter: the scene raycast is double-sided.
        uint[] indices =
        [
            0, 1, 3, 0, 3, 2, // -Z
            4, 6, 7, 4, 7, 5, // +Z
            0, 2, 6, 0, 6, 4, // -X
            1, 5, 7, 1, 7, 3, // +X
            0, 4, 5, 0, 5, 1, // -Y
            2, 3, 7, 2, 7, 6, // +Y
        ];
        return new TestMesh(positions, indices);
    }

    public static SceneNode CreateMeshNode(SceneNode parent, string name, Vector3 position, float half = 0.5f)
    {
        SceneNode node = parent.CreateChild(name);
        node.LocalPosition = position;
        node.MeshRenderer = new MeshRenderer(CreateCubeMesh(half), NoopMaterial);
        return node;
    }

    public static SceneNode CreateBrushNode(SceneNode parent, string name, Vector3 position, float half = 0.5f)
    {
        SceneNode node = parent.CreateChild(name);
        node.LocalPosition = position;
        node.Brush = Brush.CreateBox(new Vector3(-half), new Vector3(half));
        return node;
    }

    // World AABB computed without the spatial index, to check the index against.
    public static Aabb WorldBoundsOf(SceneNode node)
    {
        bool has = false;
        Aabb result = default;
        if (node.Brush is { } brush)
        {
            result = brush.LocalBounds.Transform(node.WorldMatrix);
            has = true;
        }
        if (node.MeshRenderer is { } mr)
        {
            Aabb meshBox = mr.Mesh.LocalBounds.Transform(node.WorldMatrix);
            result = has
                ? new Aabb(Vector3.Min(result.Min, meshBox.Min), Vector3.Max(result.Max, meshBox.Max))
                : meshBox;
            has = true;
        }
        has.ShouldBeTrue($"'{node.Name}' is not a spatial node");
        return result;
    }
}

// CPU-only mesh with real triangle data, for the per-triangle raycast.
internal sealed class TestMesh : Mesh
{
    public TestMesh(Vector3[] positions, uint[] indices)
    {
        Positions = positions;
        Indices = indices;
        IndexCount = (uint)indices.Length;
        LocalBounds = Aabb.FromPoints(positions);
    }

    public override void Draw()
    {
    }

    public override void DrawInstanced(InstanceBuffer instances, int instanceCount, int firstInstance = 0)
    {
    }

    public override void Dispose()
    {
    }
}

// Records uniform writes. Like the real backends it keeps state between draws,
// so a uniform a material never writes keeps the previous draw's value.
internal sealed class RecordingShaderProgram : ShaderProgram
{
    public Dictionary<string, Vector3> Vectors3 { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> Floats { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Texture> Textures { get; } = new(StringComparer.Ordinal);

    public override void Use()
    {
    }

    public override bool TryReload(PipelineBlob blob, [NotNullWhen(false)] out string? error)
    {
        error = "RecordingShaderProgram cannot reload.";
        return false;
    }

    public override void SetUniform(string name, Matrix4x4 value) { }
    public override void SetUniform(string name, Vector4 value) { }
    public override void SetUniform(string name, Vector3 value) => Vectors3[name] = value;
    public override void SetUniform(string name, Vector2 value) { }
    public override void SetUniform(string name, float value) => Floats[name] = value;
    public override void SetUniform(string name, int value) => Ints[name] = value;

    public Dictionary<string, Vector4[]> Vector4Arrays { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Matrix4x4[]> Matrix4Arrays { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> Ints { get; } = new(StringComparer.Ordinal);

    public override void SetUniform(string name, ReadOnlySpan<Vector4> values) => Vector4Arrays[name] = values.ToArray();
    public override void SetUniform(string name, ReadOnlySpan<Matrix4x4> values) => Matrix4Arrays[name] = values.ToArray();

    public override void SetTexture(string name, int unit, Texture texture) => Textures[name] = texture;

    public override void Dispose()
    {
    }
}

internal sealed class NoopShaderProgram : ShaderProgram
{
    public override void Use()
    {
    }

    public override bool TryReload(PipelineBlob blob, [NotNullWhen(false)] out string? error)
    {
        error = "NoopShaderProgram cannot reload.";
        return false;
    }

    public override void SetUniform(string name, Matrix4x4 value) { }
    public override void SetUniform(string name, Vector4 value) { }
    public override void SetUniform(string name, Vector3 value) { }
    public override void SetUniform(string name, Vector2 value) { }
    public override void SetUniform(string name, float value) { }
    public override void SetUniform(string name, int value) { }
    public override void SetUniform(string name, ReadOnlySpan<Vector4> values) { }
    public override void SetUniform(string name, ReadOnlySpan<Matrix4x4> values) { }

    public override void SetTexture(string name, int unit, Texture texture) { }

    public override void Dispose()
    {
    }
}
