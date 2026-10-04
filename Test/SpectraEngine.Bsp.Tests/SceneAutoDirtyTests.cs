using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Which <see cref="SceneNode"/> edits mark the static world dirty, and which must not.
/// </summary>
// The subtree brush counters are private, so the reparenting tests check them
// by moving each ancestor chain and watching the dirty flag.
public sealed class SceneAutoDirtyTests
{
    [Fact]
    public void Attaching_a_brush_marks_the_world_dirty()
    {
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("brush");
        scene.StaticWorldDirty.ShouldBeFalse();

        node.Brush = CreateUnitBrush();

        scene.StaticWorldDirty.ShouldBeTrue();
    }

    [Fact]
    public void Replacing_a_brush_marks_the_world_dirty()
    {
        var (scene, node, _) = CreateCleanSceneWithBrushNode();

        node.Brush = CreateUnitBrush(); // same shape, new instance

        scene.StaticWorldDirty.ShouldBeTrue();
    }

    [Fact]
    public void Detaching_a_brush_marks_the_world_dirty()
    {
        var (scene, node, _) = CreateCleanSceneWithBrushNode();

        node.Brush = null;

        scene.StaticWorldDirty.ShouldBeTrue();
    }

    [Fact]
    public void Removing_a_subtree_containing_a_brush_marks_the_world_dirty()
    {
        var (scene, node, _) = CreateCleanSceneWithBrushNode();

        scene.Root.RemoveChild(node);

        scene.StaticWorldDirty.ShouldBeTrue();
    }

    [Fact]
    public void Moving_a_brush_node_marks_the_world_dirty()
    {
        var (scene, node, _) = CreateCleanSceneWithBrushNode();

        node.LocalPosition = new Vector3(3f, 0f, 0f);

        scene.StaticWorldDirty.ShouldBeTrue();
    }

    [Fact]
    public void Moving_a_brushless_node_does_not_mark_the_world_dirty()
    {
        // A brush elsewhere in the scene keeps a compiled world live.
        var (scene, _, _) = CreateCleanSceneWithBrushNode();
        SceneNode plain = scene.Root.CreateChild("plain");
        scene.RebuildStaticWorld(new FakeRenderer());

        plain.LocalPosition = new Vector3(0f, 7f, 0f);

        scene.StaticWorldDirty.ShouldBeFalse();
    }

    [Fact]
    public void Moving_a_group_with_a_brush_descendant_marks_the_world_dirty()
    {
        var scene = new Scene("Test");
        SceneNode group = scene.Root.CreateChild("group");
        SceneNode child = group.CreateChild("brush");
        child.Brush = CreateUnitBrush();
        scene.RebuildStaticWorld(new FakeRenderer());

        group.LocalPosition = new Vector3(0f, 0f, 4f);

        scene.StaticWorldDirty.ShouldBeTrue();
    }

    [Fact]
    public void Reparenting_a_brush_node_updates_both_ancestor_chains()
    {
        var scene = new Scene("Test");
        var renderer = new FakeRenderer();
        SceneNode oldParent = scene.Root.CreateChild("old-parent");
        SceneNode newParent = scene.Root.CreateChild("new-parent");
        SceneNode brushNode = oldParent.CreateChild("brush");
        brushNode.Brush = CreateUnitBrush();
        scene.RebuildStaticWorld(renderer);

        newParent.AddChild(brushNode);
        scene.StaticWorldDirty.ShouldBeTrue();
        scene.RebuildStaticWorld(renderer);

        // Old chain no longer holds a brush.
        oldParent.LocalPosition = new Vector3(1f, 0f, 0f);
        scene.StaticWorldDirty.ShouldBeFalse();

        newParent.LocalPosition = new Vector3(0f, 1f, 0f);
        scene.StaticWorldDirty.ShouldBeTrue();
        scene.RebuildStaticWorld(renderer);

        brushNode.LocalPosition = new Vector3(0f, 0f, 1f);
        scene.StaticWorldDirty.ShouldBeTrue();
    }

    [Fact]
    public void Reparenting_a_group_with_a_brush_descendant_updates_both_ancestor_chains()
    {
        var scene = new Scene("Test");
        var renderer = new FakeRenderer();
        SceneNode oldParent = scene.Root.CreateChild("old-parent");
        SceneNode newParent = scene.Root.CreateChild("new-parent");
        SceneNode group = oldParent.CreateChild("group");
        SceneNode brushNode = group.CreateChild("brush");
        brushNode.Brush = CreateUnitBrush();
        scene.RebuildStaticWorld(renderer);

        newParent.AddChild(group);
        scene.StaticWorldDirty.ShouldBeTrue();
        scene.RebuildStaticWorld(renderer);

        oldParent.LocalPosition = new Vector3(2f, 0f, 0f);
        scene.StaticWorldDirty.ShouldBeFalse();

        newParent.LocalPosition = new Vector3(0f, 2f, 0f);
        scene.StaticWorldDirty.ShouldBeTrue();
        scene.RebuildStaticWorld(renderer);

        group.LocalPosition = new Vector3(0f, 0f, 2f);
        scene.StaticWorldDirty.ShouldBeTrue();
    }

    private static Brush CreateUnitBrush() =>
        Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));

    // Rebuilt once, so the dirty flag starts clear.
    private static (Scene Scene, SceneNode Node, FakeRenderer Renderer) CreateCleanSceneWithBrushNode()
    {
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("brush");
        node.Brush = CreateUnitBrush();

        var renderer = new FakeRenderer();
        scene.RebuildStaticWorld(renderer);
        scene.StaticWorldDirty.ShouldBeFalse();
        return (scene, node, renderer);
    }
}
