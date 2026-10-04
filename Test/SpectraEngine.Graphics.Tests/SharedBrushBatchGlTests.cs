using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Part brushes sharing one <see cref="Brush"/> instance resolve to one GPU
/// mesh, and the draw list carries one item per node. Instancing depends on it.
/// </summary>
// Real renderer: a stub handing out a new object per call would make the
// reference checks meaningless.
[Collection(GlRendererCollection.Name)]
public sealed class SharedBrushBatchGlTests
{
    private readonly GlRendererFixture _fixture;

    public SharedBrushBatchGlTests(GlRendererFixture fixture) => _fixture = fixture;

    private const int Copies = 6;

    private static Brush Box(float halfExtent = 0.5f) =>
        Brush.CreateBox(new Vector3(-halfExtent), new Vector3(halfExtent), default);

    // Spread along x so none is culled.
    private static Scene BuildScene(System.Func<int, Brush> brushFor)
    {
        var scene = new Scene();
        for (int i = 0; i < Copies; i++)
        {
            SceneNode node = scene.Root.CreateChild($"Prop{i}");
            node.LocalPosition = new Vector3(i * 2f, 0f, -10f);
            // Kind before brush: the brush setter dirties the static world, and
            // a part must not be admitted to it even for one frame.
            node.BrushKind = BrushKind.Part;
            node.Brush = brushFor(i);
        }
        return scene;
    }

    private static Camera LookingAtTheProps()
    {
        var camera = new Camera
        {
            Position = new Vector3(Copies, 0f, 10f),
            AspectRatio = 1f,
        };
        camera.LookAt(new Vector3(Copies, 0f, -10f));
        return camera;
    }

    private RenderView Draws(Scene scene)
    {
        scene.ProcessPartBrushMeshes(_fixture.Renderer);
        var view = new RenderView();
        scene.BuildRenderView(LookingAtTheProps(), view);
        return view;
    }

    [Fact]
    public void Nodes_sharing_one_brush_share_one_gpu_mesh()
    {
        Brush shared = Box();
        Scene scene = BuildScene(_ => shared);

        RenderView view = Draws(scene);

        view.PartBrushesVisible.ShouldBe(Copies);
        view.Items.Count.ShouldBe(Copies);

        var meshes = new HashSet<Mesh>();
        foreach (RenderItem item in view.Items)
            meshes.Add(item.Mesh);

        meshes.Count.ShouldBe(1, "one brush instance is one upload, however many nodes carry it");

        scene.ReleasePartBrushMeshes(_fixture.Renderer);
    }

    [Fact]
    public void Those_draws_differ_only_in_their_world_matrix()
    {
        Brush shared = Box();
        Scene scene = BuildScene(_ => shared);

        RenderView view = Draws(scene);

        var worlds = new HashSet<Matrix4x4>();
        Material? material = view.Items[0].Material;
        foreach (RenderItem item in view.Items)
        {
            worlds.Add(item.World);
            item.Material.ShouldBe(material);
        }

        worlds.Count.ShouldBe(Copies);

        scene.ReleasePartBrushMeshes(_fixture.Renderer);
    }

    [Fact]
    public void Structurally_equal_brushes_are_still_separate_uploads()
    {
        // The cache keys on reference identity; sharing between equal brushes
        // would need refcounting it does not have.
        Scene scene = BuildScene(_ => Box());

        RenderView view = Draws(scene);

        var meshes = new HashSet<Mesh>();
        foreach (RenderItem item in view.Items)
            meshes.Add(item.Mesh);

        meshes.Count.ShouldBe(Copies);

        scene.ReleasePartBrushMeshes(_fixture.Renderer);
    }

    [Fact]
    public void Retexturing_one_copy_leaves_the_others_on_the_shared_mesh()
    {
        Brush shared = Box();
        Scene scene = BuildScene(_ => shared);
        Draws(scene);

        SceneNode first = scene.Root.Children[0];
        first.Brush = shared.WithFaceMaterial(0, MaterialRegistry.Intern("Materials/other.spectramat"));

        RenderView view = Draws(scene);

        var untouched = new HashSet<Mesh>();
        var edited = new HashSet<Mesh>();
        foreach (RenderItem item in view.Items)
        {
            if (item.World.Translation == first.WorldMatrix.Translation)
                edited.Add(item.Mesh);
            else
                untouched.Add(item.Mesh);
        }

        untouched.Count.ShouldBe(1, "editing one copy must not disturb the batch the rest are in");

        // Two: the edit gave the brush a second face material, and a brush is
        // split per material.
        edited.Count.ShouldBe(2);
        edited.Overlaps(untouched).ShouldBeFalse();

        scene.ReleasePartBrushMeshes(_fixture.Renderer);
    }
}
