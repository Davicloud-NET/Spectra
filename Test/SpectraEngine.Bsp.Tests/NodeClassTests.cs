using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The built-in class a node counts as to a script and what IsA answers for
/// it: in an authored level and in a baked one, with an entity and without.
/// </summary>
public sealed class NodeClassTests
{
    private static readonly Script Spin = new("Scripts/spin.luau");

    [Fact]
    public void A_node_is_the_class_of_what_it_carries()
    {
        NodeClass.KindOf(new SceneNode("Plain")).ShouldBe(NodeClassKind.Folder);
        NodeClass.KindOf(Group()).ShouldBe(NodeClassKind.Folder);
        NodeClass.KindOf(Brush(BrushKind.World)).ShouldBe(NodeClassKind.Part);
        NodeClass.KindOf(Brush(BrushKind.Part)).ShouldBe(NodeClassKind.Part);
        NodeClass.KindOf(Mesh()).ShouldBe(NodeClassKind.MeshPart);
        NodeClass.KindOf(new SceneNode("Spin") { Script = Spin }).ShouldBe(NodeClassKind.Script);
        NodeClass.KindOf(new SceneNode("Rules") { Script = Spin with { IsModule = true } })
            .ShouldBe(NodeClassKind.ModuleScript);
    }

    [Theory]
    [InlineData(LightKind.Point, NodeClassKind.PointLight, "PointLight")]
    [InlineData(LightKind.Spot, NodeClassKind.SpotLight, "SpotLight")]
    [InlineData(LightKind.Directional, NodeClassKind.DirectionalLight, "DirectionalLight")]
    [InlineData(LightKind.Rect, NodeClassKind.SurfaceLight, "SurfaceLight")]
    [InlineData(LightKind.Disc, NodeClassKind.DiscLight, "DiscLight")]
    public void Each_kind_of_light_is_a_class_of_its_own_that_is_a_Light(LightKind light, NodeClassKind kind, string name)
    {
        var lamp = new SceneNode("Lamp") { Light = new Light { Kind = light } };

        NodeClass.KindOf(lamp).ShouldBe(kind);
        NodeClass.NameOf(kind).ShouldBe(name);
        NodeClassName.Of(lamp).ShouldBe(name);
        NodeClass.BaseOf(name).ShouldBe(NodeClass.Light);
        NodeClass.IsA(lamp, NodeClass.Light).ShouldBeTrue();
        NodeClass.IsA(lamp, NodeClass.BasePart).ShouldBeFalse();
    }

    // The classifier's order: a brush before a light before a mesh before a script.
    [Fact]
    public void A_node_that_carries_several_things_is_the_first_of_them()
    {
        SceneNode all = Mesh();
        all.Script = Spin;
        all.Light = new Light { Kind = LightKind.Spot };
        NodeClass.KindOf(all).ShouldBe(NodeClassKind.SpotLight);

        all.Brush = Core.Bsp.Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        NodeClass.KindOf(all).ShouldBe(NodeClassKind.Part);

        SceneNode meshAndScript = Mesh();
        meshAndScript.Script = Spin;
        NodeClass.KindOf(meshAndScript).ShouldBe(NodeClassKind.MeshPart);
    }

    [Theory]
    [InlineData(SceneNodeKind.BrushWorld, NodeClassKind.Part)]
    [InlineData(SceneNodeKind.BrushSubtractive, NodeClassKind.Part)]
    [InlineData(SceneNodeKind.Mesh, NodeClassKind.MeshPart)]
    public void A_baked_node_is_still_the_class_of_what_the_bake_took(SceneNodeKind baked, NodeClassKind kind)
    {
        NodeClass.KindOf(new SceneNode("Node") { BakedKind = baked }).ShouldBe(kind);
        NodeClass.KindOf(new SceneNode("Node") { BakedKind = baked, Script = Spin }).ShouldBe(kind);
        NodeClass.IsA(new SceneNode("Node") { BakedKind = baked }, NodeClass.BasePart).ShouldBeTrue();
    }

    // The loose level has a light that also draws a mesh as a light. So has the baked one.
    [Fact]
    public void A_baked_mesh_that_carries_a_light_is_the_light_as_a_live_one_is()
    {
        SceneNode live = Mesh();
        live.Light = new Light { Kind = LightKind.Point };
        var baked = new SceneNode("Baked") { BakedKind = SceneNodeKind.Mesh, Light = new Light { Kind = LightKind.Point } };

        NodeClass.KindOf(live).ShouldBe(NodeClassKind.PointLight);
        NodeClass.KindOf(baked).ShouldBe(NodeClassKind.PointLight);
    }

    [Fact]
    public void An_entity_counts_as_what_carries_it_and_one_that_carries_nothing_as_no_built_in_class()
    {
        SceneNode door = Brush(BrushKind.Part);
        door.Entity = new EntityData("func_door");
        var relay = new SceneNode("Relay") { Entity = new EntityData("logic_relay") };
        var bakedDoor = new SceneNode("Door") { BakedKind = SceneNodeKind.BrushWorld, Entity = new EntityData("func_door") };

        NodeClass.KindOf(door).ShouldBe(NodeClassKind.Part);
        NodeClass.KindOf(bakedDoor).ShouldBe(NodeClassKind.Part);
        NodeClass.KindOf(relay).ShouldBe(NodeClassKind.Instance);

        NodeClassName.Of(door).ShouldBe("func_door");
        NodeClassName.Of(bakedDoor).ShouldBe("func_door");
        NodeClassName.Of(relay).ShouldBe("logic_relay");
    }

    [Fact]
    public void A_door_is_its_entity_class_a_Part_a_BasePart_and_an_Instance()
    {
        SceneNode door = Brush(BrushKind.Part);
        door.Entity = new EntityData("func_door");

        foreach (string name in new[] { "func_door", "Part", "BasePart", "Instance" })
            NodeClass.IsA(door, name).ShouldBeTrue(name);

        foreach (string name in new[] { "MeshPart", "Folder", "Light", "logic_relay", "part", "", "Func_Door" })
            NodeClass.IsA(door, name).ShouldBeFalse(name);
    }

    [Fact]
    public void An_entity_that_carries_nothing_is_its_class_and_an_Instance_and_no_Folder()
    {
        var relay = new SceneNode("Relay") { Entity = new EntityData("logic_relay") };
        relay.CreateChild("Below");

        NodeClass.IsA(relay, "logic_relay").ShouldBeTrue();
        NodeClass.IsA(relay, NodeClass.Instance).ShouldBeTrue();
        NodeClass.IsA(relay, NodeClassName.Folder).ShouldBeFalse();
    }

    // EntityData starts with no class, and a node may be saved that way.
    [Fact]
    public void An_entity_whose_class_was_never_set_changes_nothing()
    {
        var bare = new SceneNode("Bare") { Entity = new EntityData() };
        var lamp = new SceneNode("Lamp") { Entity = new EntityData(), Light = new Light { Kind = LightKind.Point } };

        NodeClass.KindOf(bare).ShouldBe(NodeClassKind.Folder);
        NodeClassName.Of(bare).ShouldBe(NodeClassName.Folder);
        NodeClass.IsA(bare, "").ShouldBeFalse();
        NodeClassName.Of(lamp).ShouldBe("PointLight");
    }

    [Fact]
    public void Every_built_in_class_ends_at_Instance_and_a_name_that_is_none_extends_nothing()
    {
        foreach (NodeClassKind kind in Enum.GetValues<NodeClassKind>())
        {
            string? name = NodeClass.NameOf(kind);
            int steps = 0;
            while (NodeClass.BaseOf(name) is { } above)
            {
                name = above;
                (++steps).ShouldBeLessThan(4);
            }

            name.ShouldBe(NodeClass.Instance, kind.ToString());
        }

        NodeClass.BaseOf(NodeClass.Instance).ShouldBeNull();
        NodeClass.BaseOf("func_door").ShouldBeNull();
        NodeClass.BaseOf("part").ShouldBeNull();
        NodeClass.BaseOf("").ShouldBeNull();
    }

    private static SceneNode Group()
    {
        var group = new SceneNode("Group");
        group.CreateChild("Below");
        return group;
    }

    private static SceneNode Brush(BrushKind kind)
    {
        var node = new SceneNode("Brush") { BrushKind = kind };
        node.Brush = Core.Bsp.Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        return node;
    }

    private static SceneNode Mesh() =>
        new("Mesh") { MeshRenderer = new MeshRenderer(SpatialTestHelpers.CreateCubeMesh(0.5f), SpatialTestHelpers.NoopMaterial) };
}
