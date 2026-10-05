using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using static SpectraEngine.Bsp.Tests.SpatialTestHelpers;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="SceneNode.IsRendered"/>: a node with the bit off draws nothing,
/// casts nothing and holds no GPU mesh, and is otherwise the node it was.
/// </summary>
public sealed class NodeRenderFlagTests
{
    // In front of MakeCamera.
    private static readonly Vector3 InView = new(0f, 0f, -5f);

    [Fact]
    public void A_node_is_rendered_unless_it_says_otherwise()
    {
        new SceneNode("n").IsRendered.ShouldBeTrue();
    }

    [Fact]
    public void A_hidden_part_holds_no_mesh_and_adds_no_render_item_or_shadow_item()
    {
        var (scene, part, renderer) = SceneWithPart();
        scene.PartBrushMeshCount.ShouldBe(1);
        RenderItems(scene).ShouldBeGreaterThan(0);
        ShadowItems(scene).ShouldBeGreaterThan(0);

        part.IsRendered = false;
        scene.ProcessPartBrushMeshes(renderer);

        scene.PartBrushMeshCount.ShouldBe(0);
        renderer.LiveMeshes.ShouldBeEmpty("a hidden part must not keep a GPU mesh nothing draws");
        RenderItems(scene).ShouldBe(0);
        ShadowItems(scene).ShouldBe(0);
    }

    [Fact]
    public void Turning_the_bit_back_on_restores_the_mesh_and_both_draws()
    {
        var (scene, part, renderer) = SceneWithPart();
        int drawn = RenderItems(scene);
        int shadowed = ShadowItems(scene);
        part.IsRendered = false;
        scene.ProcessPartBrushMeshes(renderer);

        part.IsRendered = true;
        scene.ProcessPartBrushMeshes(renderer);

        scene.PartBrushMeshCount.ShouldBe(1);
        renderer.LiveMeshes.Count.ShouldBe(drawn);
        RenderItems(scene).ShouldBe(drawn);
        ShadowItems(scene).ShouldBe(shadowed);
    }

    [Fact]
    public void A_part_that_enters_the_scene_hidden_never_gets_a_mesh()
    {
        var scene = new Scene("Test") { StaticWorldMaterial = NoopMaterial };
        var renderer = new FakeRenderer();
        var trigger = new SceneNode("trigger")
        {
            BrushKind = BrushKind.Part,
            IsRendered = false,
            LocalPosition = InView,
            Brush = UnitBrush(),
        };

        scene.Root.AddChild(trigger);
        scene.ProcessPartBrushMeshes(renderer);

        renderer.CreatedMeshes.ShouldBeEmpty();
        RenderItems(scene).ShouldBe(0);
    }

    [Fact]
    public void A_hidden_mesh_node_is_not_drawn_and_casts_no_shadow()
    {
        var scene = new Scene("Test");
        SceneNode prop = CreateMeshNode(scene.Root, "prop", InView);
        RenderItems(scene).ShouldBe(1);
        ShadowItems(scene).ShouldBe(1);

        prop.IsRendered = false;

        RenderItems(scene).ShouldBe(0);
        ShadowItems(scene).ShouldBe(0);

        prop.IsRendered = true;

        RenderItems(scene).ShouldBe(1);
    }

    [Fact]
    public void The_bit_is_not_inherited_by_children()
    {
        var scene = new Scene("Test");
        SceneNode parent = CreateMeshNode(scene.Root, "parent", InView);
        SceneNode child = CreateMeshNode(parent, "child", new Vector3(1f, 0f, 0f));

        parent.IsRendered = false;

        var view = new RenderView();
        scene.BuildRenderView(MakeCamera(), view);
        view.Items.Count.ShouldBe(1);
        view.Items[0].Mesh.ShouldBeSameAs(child.MeshRenderer!.Mesh);
    }

    [Fact]
    public void The_bit_does_nothing_to_a_world_brush()
    {
        var scene = new Scene("Test") { StaticWorldMaterial = NoopMaterial };
        SceneNode wall = CreateBrushNode(scene.Root, "wall", InView);
        scene.RebuildStaticWorld(new FakeRenderer());

        wall.IsRendered = false;

        var view = new RenderView();
        scene.BuildRenderView(MakeCamera(), view);
        view.WorldItems.ShouldNotBeEmpty("the static world draws a world brush whatever the node says");
        scene.HiddenBrushNodes.ShouldBeEmpty();
    }

    [Fact]
    public void Flipping_the_bit_never_dirties_the_static_world_or_costs_a_compile()
    {
        var scene = new Scene("Test") { StaticWorldMaterial = NoopMaterial };
        SceneNode wall = CreateBrushNode(scene.Root, "wall", new Vector3(8f, 0f, -5f));
        SceneNode part = CreatePartNode(scene.Root, "part", InView);
        SceneNode prop = CreateMeshNode(scene.Root, "prop", new Vector3(-8f, 0f, -5f));
        var renderer = new FakeRenderer();
        scene.RebuildStaticWorld(renderer);
        int compilesBefore = scene.StaticWorldCompileCount;
        CsgWorld worldBefore = scene.StaticWorld.ShouldNotBeNull();

        for (int i = 0; i < 200; i++)
        {
            bool on = (i & 1) == 1;
            wall.IsRendered = on;
            part.IsRendered = on;
            prop.IsRendered = on;

            scene.StaticWorldDirty.ShouldBeFalse();
            scene.ProcessStaticWorldCompilation(renderer, NullLogger.Instance);
            scene.ProcessPartBrushMeshes(renderer);
        }

        scene.RebuildStaticWorldIfDirty(renderer);
        scene.StaticWorldCompileCount.ShouldBe(compilesBefore);
        scene.StaticWorld.ShouldBeSameAs(worldBefore);
    }

    [Theory]
    [InlineData(BrushKind.Part, BrushOperation.Additive, true)]
    // Neither draws anything with the bit on, so the bit hides nothing.
    [InlineData(BrushKind.Part, BrushOperation.Subtractive, false)]
    [InlineData(BrushKind.World, BrushOperation.Additive, false)]
    [InlineData(BrushKind.World, BrushOperation.Subtractive, false)]
    public void Only_an_additive_part_is_listed_as_hidden(BrushKind kind, BrushOperation operation, bool listed)
    {
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("node");
        node.BrushKind = kind;
        node.Brush = UnitBrush().WithOperation(operation);

        node.IsRendered = false;

        scene.HiddenBrushNodes.Contains(node).ShouldBe(listed);
    }

    [Fact]
    public void The_hidden_list_follows_the_bit_the_kind_the_brush_and_the_scene()
    {
        var scene = new Scene("Test");
        SceneNode part = CreatePartNode(scene.Root, "part", InView);
        scene.HiddenBrushNodes.ShouldBeEmpty();

        part.IsRendered = false;
        scene.HiddenBrushNodes.ShouldBe([part]);

        part.BrushKind = BrushKind.World;
        scene.HiddenBrushNodes.ShouldBeEmpty();
        part.BrushKind = BrushKind.Part;
        scene.HiddenBrushNodes.ShouldBe([part]);

        Brush brush = part.Brush!;
        part.Brush = null;
        scene.HiddenBrushNodes.ShouldBeEmpty();
        part.Brush = brush;
        scene.HiddenBrushNodes.ShouldBe([part]);

        scene.Root.RemoveChild(part);
        scene.HiddenBrushNodes.ShouldBeEmpty();
        scene.Root.AddChild(part);
        scene.HiddenBrushNodes.ShouldBe([part]);

        part.IsRendered = true;
        scene.HiddenBrushNodes.ShouldBeEmpty();
    }

    [Fact]
    public void A_clone_of_a_hidden_node_is_hidden()
    {
        // A copy that came back drawn would be a solid block in the doorway.
        var scene = new Scene("Test") { StaticWorldMaterial = NoopMaterial };
        var renderer = new FakeRenderer();
        var trigger = new SceneNode("trigger")
        {
            BrushKind = BrushKind.Part,
            IsRendered = false,
            LocalPosition = InView,
            Brush = UnitBrush(),
        };
        SceneNode inner = trigger.CreateChild("inner");
        inner.BrushKind = BrushKind.Part;
        inner.IsRendered = false;
        inner.Brush = UnitBrush();

        SceneNode copy = trigger.Clone();
        scene.Root.AddChild(copy);
        scene.ProcessPartBrushMeshes(renderer);

        copy.IsRendered.ShouldBeFalse();
        copy.Children[0].IsRendered.ShouldBeFalse();
        renderer.CreatedMeshes.ShouldBeEmpty();
        scene.HiddenBrushNodes.Count.ShouldBe(2);
    }

    [Fact]
    public void A_hidden_node_can_still_be_picked_and_found_by_a_query()
    {
        var (scene, part, _) = SceneWithPart();
        var ray = new Ray3(new Vector3(0f, 0f, 3f), -Vector3.UnitZ);

        part.IsRendered = false;

        scene.Raycast(ray, out SceneRaycastHit picked, SceneQueryFilter.EditorPicking).ShouldBeTrue();
        picked.Node.ShouldBeSameAs(part);

        var found = new List<SceneNode>();
        scene.GetPartBoundsInBox(new Aabb(InView - Vector3.One, InView + Vector3.One), found);
        found.ShouldContain(part);
    }

    [Fact]
    public void A_hidden_node_is_left_out_of_the_cull_totals()
    {
        // Counted in the total and never in the visible count, it would read
        // as one node culled on every frame.
        var scene = new Scene("Test") { StaticWorldMaterial = NoopMaterial };
        CreateMeshNode(scene.Root, "prop", InView);
        SceneNode hiddenProp = CreateMeshNode(scene.Root, "hiddenProp", InView);
        CreatePartNode(scene.Root, "part", InView);
        SceneNode hiddenPart = CreatePartNode(scene.Root, "hiddenPart", InView);
        hiddenProp.IsRendered = false;
        hiddenPart.IsRendered = false;
        scene.ProcessPartBrushMeshes(new FakeRenderer());

        var view = new RenderView();
        scene.BuildRenderView(MakeCamera(), view);

        view.VisibleCount.ShouldBe(1);
        view.TotalCount.ShouldBe(1);
        view.PartBrushesVisible.ShouldBe(1);
        view.PartBrushesTotal.ShouldBe(1);
    }

    private static Camera MakeCamera() => new()
    {
        Position = new Vector3(0f, 0f, 3f),
        Yaw = -MathF.PI / 2f,
        Pitch = 0f,
        AspectRatio = 16f / 9f,
    };

    private static Brush UnitBrush() => Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));

    private static SceneNode CreatePartNode(SceneNode parent, string name, Vector3 position)
    {
        SceneNode node = parent.CreateChild(name);
        node.LocalPosition = position;
        node.BrushKind = BrushKind.Part;
        node.Brush = UnitBrush();
        return node;
    }

    // One part in view, its mesh already built.
    private static (Scene Scene, SceneNode Part, FakeRenderer Renderer) SceneWithPart()
    {
        var scene = new Scene("Test") { StaticWorldMaterial = NoopMaterial };
        SceneNode part = CreatePartNode(scene.Root, "part", InView);
        var renderer = new FakeRenderer();
        scene.ProcessPartBrushMeshes(renderer);
        return (scene, part, renderer);
    }

    private static int RenderItems(Scene scene)
    {
        var view = new RenderView();
        scene.BuildRenderView(MakeCamera(), view);
        return view.Items.Count;
    }

    private static int ShadowItems(Scene scene)
    {
        var view = new RenderView();
        scene.BuildShadowView(MakeCamera().GetViewProjection(), view);
        return view.Items.Count;
    }
}
