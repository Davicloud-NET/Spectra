using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>What the editor hands to the renderer's outline pass.</summary>
public sealed class OutlineMeshesTests
{
    [Fact]
    public void A_brush_gets_a_mesh_built_once_and_reused()
    {
        var renderer = new CompilingRenderer();
        var meshes = new OutlineMeshes(renderer);
        SceneNode node = BrushNode(new Scene("s"), "Block");

        meshes.BeginFrame();
        meshes.TryAdd(node, OutlineGroup.Selected).ShouldBeTrue();
        meshes.EndFrame();

        meshes.BeginFrame();
        meshes.TryAdd(node, OutlineGroup.Selected).ShouldBeTrue();
        meshes.EndFrame();

        renderer.CreatedMeshCount.ShouldBe(1, "the same brush instance must not be rebuilt every frame");
        renderer.Outlines.Count.ShouldBe(2);
        renderer.Outlines.Items[0].World.ShouldBe(node.WorldMatrix);
        renderer.Outlines.Items[0].Group.ShouldBe(OutlineGroup.Selected);
    }

    [Fact]
    public void A_brush_nothing_asked_for_is_freed_at_the_end_of_the_frame()
    {
        // An edit swaps the brush for a new instance every frame of a drag, so
        // a mesh that outlived its frame would pile up one per frame.
        var renderer = new CompilingRenderer();
        var meshes = new OutlineMeshes(renderer);
        SceneNode node = BrushNode(new Scene("s"), "Block");

        meshes.BeginFrame();
        meshes.TryAdd(node, OutlineGroup.Selected);
        meshes.EndFrame();
        meshes.BrushMeshCount.ShouldBe(1);

        meshes.BeginFrame();
        meshes.EndFrame();

        meshes.BrushMeshCount.ShouldBe(0);
        renderer.MeshesDestroyed.ShouldBe(1);
    }

    [Fact]
    public void A_group_is_outlined_as_the_things_in_it()
    {
        var renderer = new CompilingRenderer();
        var meshes = new OutlineMeshes(renderer);
        var scene = new Scene("s");

        SceneNode group = scene.Root.CreateChild("Group");
        BrushNode(scene, "A", group);
        BrushNode(scene, "B", group);
        SceneNode inner = group.CreateChild("Inner");
        BrushNode(scene, "C", inner);

        meshes.BeginFrame();
        meshes.TryAdd(group, OutlineGroup.Hovered).ShouldBeFalse("a group has no shape of its own");
        meshes.AddSubtree(group, OutlineGroup.Hovered).ShouldBe(3);
        meshes.EndFrame();

        renderer.Outlines.Count.ShouldBe(3);
        renderer.Outlines.Items.ShouldAllBe(item => item.Group == OutlineGroup.Hovered);
    }

    [Fact]
    public void Release_frees_every_brush_mesh()
    {
        var renderer = new CompilingRenderer();
        var meshes = new OutlineMeshes(renderer);
        var scene = new Scene("s");

        meshes.BeginFrame();
        meshes.TryAdd(BrushNode(scene, "A"), OutlineGroup.Selected);
        meshes.TryAdd(BrushNode(scene, "B"), OutlineGroup.Selected);
        meshes.EndFrame();

        meshes.Release();

        meshes.BrushMeshCount.ShouldBe(0);
        renderer.MeshesDestroyed.ShouldBe(2);
    }

    [Fact]
    public void The_list_refuses_past_its_cap_and_says_how_many()
    {
        var renderer = new CompilingRenderer();
        var meshes = new OutlineMeshes(renderer);
        SceneNode node = BrushNode(new Scene("s"), "Block");

        meshes.BeginFrame();
        for (int i = 0; i < OutlineList.MaxItems + 5; i++)
            meshes.TryAdd(node, OutlineGroup.Selected);
        meshes.EndFrame();

        renderer.Outlines.Count.ShouldBe(OutlineList.MaxItems);
        renderer.Outlines.Dropped.ShouldBe(5);
    }

    private static SceneNode BrushNode(Scene scene, string name, SceneNode? parent = null)
    {
        SceneNode node = (parent ?? scene.Root).CreateChild(name);
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        return node;
    }
}
