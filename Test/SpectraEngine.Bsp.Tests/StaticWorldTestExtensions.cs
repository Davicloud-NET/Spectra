using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// For suites that compile single-material worlds. These assert the chunk has
// one submesh, so a suite that grows a second material fails here.
internal static class StaticWorldTestExtensions
{
    public static Mesh SingleMesh(this StaticWorldChunkMesh chunk) =>
        chunk.Submeshes.ShouldHaveSingleItem().Mesh;

    public static FakeMesh SingleFakeMesh(this StaticWorldChunkMesh chunk) =>
        (FakeMesh)chunk.SingleMesh();

    public static Material? SingleMaterial(this StaticWorldChunkMesh chunk) =>
        chunk.Submeshes.ShouldHaveSingleItem().Material;
}
