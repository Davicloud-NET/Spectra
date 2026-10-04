using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Models;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using System;
using System.IO;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Loading a cooked <c>.smodel</c> in place of its authored model.</summary>
// Fixtures come from HandBuiltSmodel, not the cooker, so the reader is held to
// the spec and not to its own writer. No .gltf is written: a load that fell
// back to the authored file would otherwise pass.
public class CookedModelTests : IDisposable
{
    private const string Authored = "Models/prop.gltf";
    private const string Cooked = "Models/prop.smodel";
    private const string Material = "Materials/hand.spectramat";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SpectraCookedModelTests", Guid.NewGuid().ToString("N"));

    public CookedModelTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Models"));
        Directory.CreateDirectory(Path.Combine(_root, "Materials"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_cooked_file_wins_where_one_exists_and_the_authored_path_stays_the_name()
    {
        ModelContentPath.CookedPathFor(Authored).ShouldBe(Cooked);
        ModelContentPath.CookedPathFor(Cooked).ShouldBe(Cooked);
        ModelContentPath.IsCooked(Cooked).ShouldBeTrue();
        ModelContentPath.IsCooked(Authored).ShouldBeFalse();

        var empty = new ContentSourceStack();
        ModelContentPath.Resolve(empty, Authored).ShouldBe(Authored, "a miss names the file it looked for");

        Write(Cooked, Model((0u, 3u, 0u)));
        ModelContentPath.Resolve(Stack(), Authored).ShouldBe(Cooked);
    }

    [Fact]
    public void A_cooked_model_loads_with_one_gpu_mesh_per_submesh()
    {
        Write(Cooked, Model((0u, 3u, 0u), (3u, 3u, 0u)));
        WriteMaterial();

        using AssetManager assets = Attach(out FakeRenderer renderer);

        assets.IsModelCooked(Authored).ShouldBeTrue();

        ModelAsset model = assets.LoadModel(Authored);
        ModelData data = model.Data.ShouldNotBeNull();

        model.Error.ShouldBeNull();
        data.Meshes.Count.ShouldBe(2);
        renderer.CreatedMeshes.Count.ShouldBe(1);
        model.Data!.Meshes[0].Geometry.ShouldBeSameAs(model.Data.Meshes[1].Geometry);
        model.Meshes[0].Positions.ShouldBeSameAs(model.Meshes[1].Positions);

        // Bounds come from the header, not from a vertex walk.
        data.LocalBounds.Min.ShouldBe(new Vector3(-1f, -2f, -3f));
        data.LocalBounds.Max.ShouldBe(new Vector3(4f, 5f, 6f));

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void Each_submesh_gets_a_zero_based_slice_of_the_shared_vertex_buffer()
    {
        // The file has one vertex buffer with submeshes as index ranges.
        // ModelMesh wants a zero-based array per submesh, so the load rebases
        // each range on its minimum index.
        Write(Cooked, Model((0u, 3u, 0u), (3u, 3u, 0u)));

        using AssetManager assets = Attach(out _);
        ModelData data = assets.LoadModel(Authored).Data.ShouldNotBeNull();

        data.Meshes[0].Indices.ShouldBe([0u, 1u, 2u]);
        data.Meshes[0].VertexCount.ShouldBe(3);
        data.Meshes[0].Vertices[0].ShouldBe(0f, 1e-5f);

        // Second range names vertices 3 to 5.
        data.Meshes[1].Indices.ShouldBe([0u, 1u, 2u]);
        data.Meshes[1].VertexCount.ShouldBe(3);
        data.Meshes[1].Vertices[0].ShouldBe(30f, 1e-5f);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_submesh_binds_the_material_the_file_named_by_path()
    {
        Write(Cooked, Model((0u, 3u, 0u)));
        WriteMaterial();

        using AssetManager assets = Attach(out _);
        ModelAsset model = assets.LoadModel(Authored);
        ModelData data = model.Data.ShouldNotBeNull();

        Material bound = model.MaterialFor(data.Meshes[0]);
        bound.ShouldNotBeSameAs(assets.DefaultMaterial);
        bound.Name.ShouldBe("hand");
        bound.SourcePath.ShouldBe(Material);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_submesh_that_names_no_material_degrades_to_the_default_one()
    {
        Write(Cooked, Model((0u, 3u, HandBuiltSmodel.NameOffsetAbsent)));

        using AssetManager assets = Attach(out _);
        ModelAsset model = assets.LoadModel(Authored);

        model.MaterialFor(model.Data.ShouldNotBeNull().Meshes[0]).ShouldBeSameAs(assets.DefaultMaterial);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_layout_this_build_cannot_upload_is_refused_naming_both_ids()
    {
        // An 11-float layout with a tangent, which this build has no upload path for.
        byte[] file = new HandBuiltSmodel()
            .VertexLayout(
                strideFloats: 11,
                (Semantic: (byte)0, ComponentType: (byte)0, ComponentCount: (byte)3, ByteOffset: (ushort)0),
                (Semantic: (byte)1, ComponentType: (byte)0, ComponentCount: (byte)3, ByteOffset: (ushort)12),
                (Semantic: (byte)2, ComponentType: (byte)0, ComponentCount: (byte)4, ByteOffset: (ushort)24),
                (Semantic: (byte)3, ComponentType: (byte)0, ComponentCount: (byte)2, ByteOffset: (ushort)40))
            .VertexBuffer(new float[11 * 3])
            .Indices16(0, 1, 2)
            .Submeshes((0u, 3u, HandBuiltSmodel.NameOffsetAbsent))
            .Build();

        Write(Cooked, file);

        using AssetManager assets = Attach(out _);

        SmodelFormatException refused = Should.Throw<SmodelFormatException>(() => assets.LoadModel(Authored));
        refused.Message.ShouldContain(SmodelStandardLayout.LayoutId.ToString("X8"));
        refused.Message.ShouldContain("Recook");

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void With_no_cooked_file_the_authored_one_is_imported_as_before()
    {
        using var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(new FakeRenderer());

        assets.IsModelCooked("Models/crate.obj").ShouldBeFalse();
        assets.LoadModel("Models/crate.obj").Data.ShouldNotBeNull().Meshes.Count.ShouldBe(2);

        assets.ReleaseGraphicsResources();
    }

    // Six vertices, two triangles. Vertex v has x = v * 10, so a slice starting
    // at the wrong vertex shows up as a different number.
    private static byte[] Model(params (uint Start, uint Count, uint MaterialName)[] submeshes)
    {
        var vertices = new float[6 * 8];
        for (int v = 0; v < 6; v++)
        {
            vertices[v * 8] = v * 10f;
            vertices[(v * 8) + 5] = 1f;
            vertices[(v * 8) + 6] = v * 0.125f;
        }

        return new HandBuiltSmodel()
            .VertexLayout(
                strideFloats: 8,
                (Semantic: (byte)0, ComponentType: (byte)0, ComponentCount: (byte)3, ByteOffset: (ushort)0),
                (Semantic: (byte)1, ComponentType: (byte)0, ComponentCount: (byte)3, ByteOffset: (ushort)12),
                (Semantic: (byte)3, ComponentType: (byte)0, ComponentCount: (byte)2, ByteOffset: (ushort)24))
            .VertexBuffer(vertices)
            .Indices16(0, 1, 2, 3, 4, 5)
            .Submeshes(submeshes)
            .Names(out _, Material)
            .Build();
    }

    private void WriteMaterial()
    {
        File.WriteAllText(
            Path.Combine(_root, "Materials", "hand.spectramat"), "shader = lit\ncolor uBaseColor = #FFFFFF\n");
    }

    private void Write(string contentPath, byte[] bytes) =>
        File.WriteAllBytes(Path.Combine(_root, contentPath.Replace('/', Path.DirectorySeparatorChar)), bytes);

    private ContentSourceStack Stack()
    {
        var stack = new ContentSourceStack();
        stack.Mount(new LooseFileSource(NullLogger.Instance, _root));
        return stack;
    }

    private AssetManager Attach(out FakeRenderer renderer)
    {
        renderer = new FakeRenderer();
        var assets = new AssetManager(NullLogger<AssetManager>.Instance, _root, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);
        return assets;
    }
}
