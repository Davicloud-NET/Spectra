using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The switches a script sets on a part or a mesh through
/// <see cref="ScriptWrites"/>: solid, found by a ray, touched, drawn. What is
/// taken, what a world brush refuses, and that Stop puts each back.
/// </summary>
public sealed class PartSwitchWritesTests
{
    private static readonly SettableMemberId[] Switches =
    [
        SettableMemberId.CanCollide, SettableMemberId.CanQuery, SettableMemberId.CanTouch, SettableMemberId.Visible,
    ];

    private readonly Scene _scene = new("Switches");

    [Fact]
    public void Each_switch_of_a_part_reads_back_what_was_written()
    {
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Crate", BrushKind.Part, Vector3.Zero);
        var writes = new ScriptWrites(Started());

        foreach (SettableMemberId member in Switches)
        {
            Write(writes, part, member, MemberValue.Of(false));
            ScriptWrites.Read(part, member).ShouldBe(MemberValue.Of(false), member.ToString());
        }

        part.CanCollide.ShouldBeFalse();
        part.CanQuery.ShouldBeFalse();
        part.CanTouch.ShouldBeFalse();
        part.IsRendered.ShouldBeFalse();
        part.Anchored.ShouldBeTrue();
    }

    [Fact]
    public void A_mesh_has_the_same_switches()
    {
        SceneNode prop = SpatialTestHelpers.CreateMeshNode(_scene.Root, "Prop", Vector3.Zero);
        var writes = new ScriptWrites(Started());

        foreach (SettableMemberId member in Switches)
        {
            Write(writes, prop, member, MemberValue.Of(false));
            ScriptWrites.Read(prop, member).ShouldBe(MemberValue.Of(false), member.ToString());
        }
    }

    [Fact]
    public void A_part_that_is_not_visible_is_not_drawn_and_is_drawn_again_when_it_is()
    {
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Crate", BrushKind.Part, Vector3.Zero);
        var writes = new ScriptWrites(Started());
        _scene.DrawableNodes.ShouldContain(part);

        Write(writes, part, SettableMemberId.Visible, MemberValue.Of(false));
        _scene.DrawableNodes.ShouldNotContain(part);

        Write(writes, part, SettableMemberId.Visible, MemberValue.Of(true));
        _scene.DrawableNodes.ShouldContain(part);
    }

    [Fact]
    public void A_ray_does_not_find_a_part_whose_CanQuery_is_off_and_finds_it_again_when_it_is_on()
    {
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Crate", BrushKind.Part, Vector3.Zero);
        var writes = new ScriptWrites(Started());
        var ray = new Ray3(new Vector3(0f, 5f, 0f), -Vector3.UnitY);
        _scene.Raycast(in ray, out SceneRaycastHit found).ShouldBeTrue();
        found.Node.ShouldBeSameAs(part);

        Write(writes, part, SettableMemberId.CanQuery, MemberValue.Of(false));
        _scene.Raycast(in ray, out _).ShouldBeFalse();

        Write(writes, part, SettableMemberId.CanQuery, MemberValue.Of(true));
        _scene.Raycast(in ray, out _).ShouldBeTrue();
    }

    [Fact]
    public void Stop_puts_back_every_switch()
    {
        SceneNode trigger = EntityMotion.BrushNode(_scene.Root, "Zone", BrushKind.Part, Vector3.Zero);
        trigger.CanCollide = false;
        trigger.IsRendered = false;
        PhysicsFlags authored = trigger.PhysicsFlags;
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);

        Write(writes, trigger, SettableMemberId.CanCollide, MemberValue.Of(true));
        Write(writes, trigger, SettableMemberId.CanQuery, MemberValue.Of(false));
        Write(writes, trigger, SettableMemberId.CanTouch, MemberValue.Of(false));
        Write(writes, trigger, SettableMemberId.CanTouch, MemberValue.Of(true));
        Write(writes, trigger, SettableMemberId.Visible, MemberValue.Of(true));
        world.Journal.Count.ShouldBe(4);

        world.Deactivate();

        trigger.PhysicsFlags.ShouldBe(authored);
        trigger.IsRendered.ShouldBeFalse();
        _scene.DrawableNodes.ShouldNotContain(trigger);
    }

    [Fact]
    public void A_write_of_the_value_a_switch_already_has_records_nothing()
    {
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Crate", BrushKind.Part, Vector3.Zero);
        part.CanTouch = false;
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);

        foreach (SettableMemberId member in Switches)
            Write(writes, part, member, ScriptWrites.Read(part, member));

        world.Journal.Count.ShouldBe(0);
    }

    [Fact]
    public void A_world_brush_refuses_every_switch_and_says_to_use_a_part()
    {
        SceneNode wall = EntityMotion.BrushNode(_scene.Root, "Wall", BrushKind.World, Vector3.Zero);
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);

        foreach (SettableMemberId member in Switches)
        {
            writes.TryWrite(wall, member, MemberValue.Of(false), out string? refusal).ShouldBeFalse();

            refusal.ShouldBe(
                $"Wall cannot change its {member}: it is a world brush, fused into the level, and only parts can " +
                "change while a level plays. Convert the brush to a part.");
        }

        // Also the value it has: the write is the mistake, whatever it holds.
        writes.TryWrite(wall, SettableMemberId.CanCollide, MemberValue.Of(true), out _).ShouldBeFalse();
        wall.PhysicsFlags.ShouldBe(PhysicsFlags.Default);
        wall.IsRendered.ShouldBeTrue();
        world.Journal.Count.ShouldBe(0);
    }

    // A baked level's node has no brush. It says what the bake took from it.
    [Theory]
    [InlineData(SceneNodeKind.BrushWorld)]
    [InlineData(SceneNodeKind.BrushSubtractive)]
    public void A_world_brush_of_a_baked_level_refuses_in_the_same_words(SceneNodeKind baked)
    {
        var wall = new SceneNode("Wall") { BakedKind = baked };
        _scene.Root.AddChild(wall);
        var writes = new ScriptWrites(Started());

        writes.TryWrite(wall, SettableMemberId.Visible, MemberValue.Of(false), out string? refusal).ShouldBeFalse();

        refusal.ShouldBe(
            "Wall cannot change its Visible: it is a world brush, fused into the level, and only parts can " +
            "change while a level plays. Convert the brush to a part.");
        ScriptWrites.Read(wall, SettableMemberId.Visible).ShouldBe(MemberValue.Of(true));
    }

    // No switch is inherited, so the world brush below changes nothing for
    // the part above it.
    [Fact]
    public void A_part_above_a_world_brush_takes_a_write_to_its_own_switches()
    {
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Frame", BrushKind.Part, Vector3.Zero);
        EntityMotion.BrushNode(part, "Wall", BrushKind.World, new Vector3(0f, 2f, 0f));
        part.HoldsWorldBrush.ShouldBeTrue();

        Write(new ScriptWrites(Started()), part, SettableMemberId.CanCollide, MemberValue.Of(false));

        part.CanCollide.ShouldBeFalse();
    }

    [Fact]
    public void A_light_and_a_folder_have_no_switch_of_a_part()
    {
        SceneNode lamp = _scene.Root.CreateChild("Lamp");
        lamp.Light = new Light { Kind = LightKind.Point };
        SceneNode folder = _scene.Root.CreateChild("Props");
        var writes = new ScriptWrites(Started());

        writes.TryWrite(lamp, SettableMemberId.CanCollide, MemberValue.Of(false), out string? ofTheLamp).ShouldBeFalse();
        writes.TryWrite(folder, SettableMemberId.Visible, MemberValue.Of(false), out string? ofTheFolder).ShouldBeFalse();

        ofTheLamp.ShouldBe("CanCollide is not a valid member of PointLight \"Lamp\"");
        ofTheFolder.ShouldBe("Visible is not a valid member of Folder \"Props\"");
        ScriptWrites.Read(folder, SettableMemberId.CanTouch).ShouldBe(MemberValue.None);
        lamp.CanCollide.ShouldBeTrue();
    }

    [Fact]
    public void A_value_that_is_no_boolean_is_refused_in_Robloxs_words()
    {
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Crate", BrushKind.Part, Vector3.Zero);

        new ScriptWrites(Started()).TryWrite(part, SettableMemberId.CanCollide, MemberValue.Of(0.0), out string? refusal)
            .ShouldBeFalse();

        refusal.ShouldBe("Unable to assign property CanCollide. boolean expected, got number");
        part.CanCollide.ShouldBeTrue();
    }

    private EntityWorld Started()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        return world;
    }

    private static void Write(ScriptWrites writes, SceneNode node, SettableMemberId member, MemberValue value) =>
        writes.TryWrite(node, member, in value, out string? refusal).ShouldBeTrue(refusal);
}
