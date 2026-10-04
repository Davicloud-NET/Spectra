using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="Scene.RebuildStaticWorld"/> rejects brush nodes whose world transform
/// is scaled, singular or non-finite.
/// </summary>
// The renderer is null on purpose: a correct rebuild throws during validation,
// before it would touch one.
public sealed class SceneStaticWorldTests
{
    private static Scene CreateSceneWithBrushNode(out SceneNode brushNode)
    {
        var scene = new Scene("Test");
        brushNode = scene.Root.CreateChild("brush");
        brushNode.Brush = Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        return scene;
    }

    [Fact]
    public void Scaled_brush_node_is_rejected()
    {
        Scene scene = CreateSceneWithBrushNode(out SceneNode node);
        node.LocalScale = new Vector3(2f, 1f, 1f);

        Should.Throw<InvalidOperationException>(() => scene.RebuildStaticWorld(null!));
    }

    [Fact]
    public void Brush_node_under_a_scaled_parent_is_rejected()
    {
        var scene = new Scene("Test");
        SceneNode parent = scene.Root.CreateChild("parent");
        parent.LocalScale = new Vector3(1f, 3f, 1f);
        SceneNode child = parent.CreateChild("brush");
        child.Brush = Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));

        Should.Throw<InvalidOperationException>(() => scene.RebuildStaticWorld(null!));
    }

    [Fact]
    public void Singular_brush_node_transform_is_rejected()
    {
        Scene scene = CreateSceneWithBrushNode(out SceneNode node);
        node.LocalScale = Vector3.Zero;

        Should.Throw<InvalidOperationException>(() => scene.RebuildStaticWorld(null!));
    }

    [Fact]
    public void Brush_node_with_NaN_scale_is_rejected()
    {
        // NaN comparisons are all false, so a tolerance check alone would
        // pass a NaN basis as rigid.
        Scene scene = CreateSceneWithBrushNode(out SceneNode node);
        node.LocalScale = new Vector3(float.NaN, 1f, 1f);

        Should.Throw<InvalidOperationException>(() => scene.RebuildStaticWorld(null!));
    }

    [Fact]
    public void Brush_node_with_NaN_position_is_rejected()
    {
        // Translation never reaches the basis checks.
        Scene scene = CreateSceneWithBrushNode(out SceneNode node);
        node.LocalPosition = new Vector3(0f, float.NaN, 0f);

        Should.Throw<InvalidOperationException>(() => scene.RebuildStaticWorld(null!));
    }

    [Fact]
    public void Rebuild_without_brush_nodes_yields_null_world_and_clears_dirty()
    {
        var scene = new Scene("Test");
        scene.Root.CreateChild("empty");
        scene.MarkStaticWorldDirty();

        scene.RebuildStaticWorld(null!);

        scene.StaticWorld.ShouldBeNull();
        scene.StaticWorldDirty.ShouldBeFalse();
    }
}
