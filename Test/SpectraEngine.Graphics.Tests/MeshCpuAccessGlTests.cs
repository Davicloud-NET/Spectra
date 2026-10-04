using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The opt-in CPU mirror on <see cref="Mesh"/>, against a real GL mesh.
/// </summary>
[Collection(GlRendererCollection.Name)]
public sealed class MeshCpuAccessGlTests
{
    private readonly GlRendererFixture _fixture;

    public MeshCpuAccessGlTests(GlRendererFixture fixture) => _fixture = fixture;

    // position, normal, uv
    private static readonly float[] Vertices =
    [
        0f, 0f, 0f,   0f, 0f, 1f,   0f, 0f,
        2f, 0f, 0f,   0f, 0f, 1f,   1f, 0f,
        0f, 3f, 0f,   0f, 0f, 1f,   0f, 1f,
    ];

    private static readonly uint[] Indices = [0, 1, 2];

    [Fact]
    public void A_retained_mesh_keeps_positions_normals_and_indices()
    {
        Mesh mesh = _fixture.Renderer.CreateMesh(Vertices, Indices, VertexAttribute.StandardLayout);
        try
        {
            mesh.Positions.Count.ShouldBe(3);
            mesh.Normals.Count.ShouldBe(3);
            mesh.Indices.Count.ShouldBe(3);
            mesh.Positions[2].Y.ShouldBe(3f);
        }
        finally
        {
            _fixture.Renderer.DestroyMesh(mesh);
        }
    }

    [Fact]
    public void A_gpu_only_mesh_keeps_no_arrays_but_still_measures_itself()
    {
        Mesh mesh = _fixture.Renderer.CreateMesh(
            Vertices, Indices, VertexAttribute.StandardLayout, MeshCpuAccess.None);
        try
        {
            mesh.Positions.Count.ShouldBe(0);
            mesh.Normals.Count.ShouldBe(0);
            mesh.Indices.Count.ShouldBe(0);

            // Culling and framing read bounds for every mesh.
            mesh.LocalBounds.Min.ShouldBe(new System.Numerics.Vector3(0f, 0f, 0f));
            mesh.LocalBounds.Max.ShouldBe(new System.Numerics.Vector3(2f, 3f, 0f));
            mesh.IndexCount.ShouldBe(3u);
        }
        finally
        {
            _fixture.Renderer.DestroyMesh(mesh);
        }
    }

    [Fact]
    public void Retained_and_gpu_only_bounds_agree()
    {
        Mesh retained = _fixture.Renderer.CreateMesh(Vertices, Indices, VertexAttribute.StandardLayout);
        Mesh gpuOnly = _fixture.Renderer.CreateMesh(
            Vertices, Indices, VertexAttribute.StandardLayout, MeshCpuAccess.None);
        try
        {
            gpuOnly.LocalBounds.Min.ShouldBe(retained.LocalBounds.Min);
            gpuOnly.LocalBounds.Max.ShouldBe(retained.LocalBounds.Max);
        }
        finally
        {
            _fixture.Renderer.DestroyMesh(gpuOnly);
            _fixture.Renderer.DestroyMesh(retained);
        }
    }
}
