using System.Numerics;
using System.Text;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The one way a script changes a node: what it lists, what it refuses, and
/// that stopping the level puts every write back.
/// </summary>
public sealed class ScriptWritesTests
{
    private readonly Scene _scene = new("Writes");

    [Fact]
    public void The_list_is_in_the_order_of_its_ids_and_names_each_member_once()
    {
        IReadOnlyList<SettableMember> members = ScriptWrites.Settable;

        members.Count.ShouldBe(Enum.GetValues<SettableMemberId>().Length);
        for (int i = 0; i < members.Count; i++)
        {
            members[i].Id.ShouldBe((SettableMemberId)i);
            members[i].Name.ShouldBe(members[i].Id.ToString());
            members[i].Kind.ShouldNotBe(MemberValueKind.None);
        }
    }

    [Fact]
    public void A_member_is_found_by_its_name_and_no_other_name_is()
    {
        foreach (SettableMember member in ScriptWrites.Settable)
        {
            ScriptWrites.TryFind(Encoding.UTF8.GetBytes(member.Name), out SettableMember found).ShouldBeTrue();
            found.ShouldBe(member);
        }

        ScriptWrites.TryFind("position"u8, out _).ShouldBeFalse();
        ScriptWrites.TryFind("ClassName"u8, out _).ShouldBeFalse();
        ScriptWrites.TryFind(""u8, out _).ShouldBeFalse();
    }

    [Fact]
    public void Each_member_reads_back_what_was_written_and_Stop_puts_the_node_back()
    {
        SceneNode parent = _scene.Root.CreateChild("Rig");
        parent.LocalPosition = new Vector3(10f, 0f, 0f);
        SceneNode part = EntityMotion.BrushNode(parent, "Door", BrushKind.Part, new Vector3(1f, 2f, 3f));
        byte[] authored = EntityMotion.Bits(part.LocalTransform);
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);
        CFrame turned = new CFrame(4f, 5f, 6f) * CFrame.Angles(0.1f, 0.2f, 0.3f);

        Write(writes, part, SettableMemberId.Position, MemberValue.Of(new Vector3(7f, 8f, 9f)));
        ScriptWrites.Read(part, SettableMemberId.Position).ShouldBe(MemberValue.Of(new Vector3(7f, 8f, 9f)));
        part.LocalPosition.ShouldBe(new Vector3(-3f, 8f, 9f));

        Write(writes, part, SettableMemberId.LocalPosition, MemberValue.Of(new Vector3(1f, 1f, 1f)));
        ScriptWrites.Read(part, SettableMemberId.LocalPosition).ShouldBe(MemberValue.Of(new Vector3(1f, 1f, 1f)));

        Write(writes, part, SettableMemberId.LocalCFrame, MemberValue.Of(turned));
        ScriptWrites.Read(part, SettableMemberId.LocalCFrame).ShouldBe(MemberValue.Of(turned));

        Write(writes, part, SettableMemberId.CFrame, MemberValue.Of(turned));
        ScriptWrites.Read(part, SettableMemberId.CFrame).Frame.FuzzyEq(turned).ShouldBeTrue();

        Write(writes, part, SettableMemberId.Name, MemberValue.Of("Gate"));
        ScriptWrites.Read(part, SettableMemberId.Name).ShouldBe(MemberValue.Of("Gate"));

        world.MovedNodeCount.ShouldBe(1);
        world.Journal.Count.ShouldBe(2);

        world.Deactivate();

        EntityMotion.Bits(part.LocalTransform).ShouldBe(authored);
        part.Name.ShouldBe("Door");
        world.Journal.Count.ShouldBe(0);
    }

    [Fact]
    public void A_value_of_the_wrong_kind_is_refused_in_Robloxs_words()
    {
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Door", BrushKind.Part, Vector3.Zero);
        var writes = new ScriptWrites(Started());

        writes.TryWrite(part, SettableMemberId.Position, MemberValue.Of("here"), out string? first).ShouldBeFalse();
        writes.TryWrite(part, SettableMemberId.Name, MemberValue.Of(Vector3.One), out string? second).ShouldBeFalse();
        writes.TryWrite(part, SettableMemberId.CFrame, MemberValue.None, out string? third).ShouldBeFalse();

        first.ShouldBe("Unable to assign property Position. Vector3 expected, got string");
        second.ShouldBe("Unable to assign property Name. string expected, got Vector3");
        third.ShouldBe("Unable to assign property CFrame. CFrame expected, got nil");
        part.LocalPosition.ShouldBe(Vector3.Zero);
        part.Name.ShouldBe("Door");
    }

    [Fact]
    public void A_refused_write_changes_nothing_and_records_nothing()
    {
        SceneNode wall = EntityMotion.BrushNode(_scene.Root, "Wall", BrushKind.World, Vector3.Zero);
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Door", BrushKind.Part, Vector3.Zero);
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);

        writes.TryWrite(wall, SettableMemberId.Position, MemberValue.Of(Vector3.One), out string? moved).ShouldBeFalse();
        writes.TryWrite(part, SettableMemberId.LocalPosition, MemberValue.Of(new Vector3(float.NaN, 0f, 0f)), out string? nowhere)
            .ShouldBeFalse();
        writes.TryWrite(part, SettableMemberId.Name, MemberValue.Of(""), out string? unnamed).ShouldBeFalse();

        moved.ShouldNotBeNull().ShouldStartWith("Wall cannot be moved: a world brush sits at or below it");
        nowhere.ShouldBe("Door cannot be moved: a position must be three numbers, with none of them infinite.");
        unnamed.ShouldBe("Door cannot be renamed: a name cannot be empty.");
        world.Journal.Count.ShouldBe(0);
        wall.LocalPosition.ShouldBe(Vector3.Zero);
        part.LocalPosition.ShouldBe(Vector3.Zero);
    }

    [Fact]
    public void A_write_to_a_level_that_has_stopped_is_refused()
    {
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Door", BrushKind.Part, Vector3.Zero);
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);
        world.Deactivate();

        writes.TryWrite(part, SettableMemberId.Name, MemberValue.Of("Late"), out string? refusal).ShouldBeFalse();

        refusal.ShouldBe("Door cannot be changed: the level has stopped.");
        part.Name.ShouldBe("Door");
    }

    [Fact]
    public void An_entitys_move_and_a_scripts_write_of_one_node_are_one_entry()
    {
        SceneNode door = EntityMotion.Slider(
            EntityMotion.BrushNode(_scene.Root, "Door", BrushKind.Part, new Vector3(0f, 0.5f, 0f)),
            new Vector3(0f, 2f, 0f), ticks: 20);
        byte[] authored = EntityMotion.Bits(door.LocalTransform);
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);

        world.Tick(EntityMotion.Dt);
        Write(writes, door, SettableMemberId.Position, MemberValue.Of(new Vector3(5f, 5f, 5f)));
        world.Tick(EntityMotion.Dt);

        world.Journal.Count.ShouldBe(1);

        world.Deactivate();

        EntityMotion.Bits(door.LocalTransform).ShouldBe(authored);
    }

    [Fact]
    public void A_renamed_entity_is_found_by_its_new_name_and_by_its_old_one_after_Stop()
    {
        var log = new List<string>();
        SceneNode node = EntityRuntime.Place(_scene.Root, "Speaker", "recorder");
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityMotion.Catalog(log));
        world.Activate();
        var writes = new ScriptWrites(world);
        var found = new List<Entity>();

        Write(writes, node, SettableMemberId.Name, MemberValue.Of("Herald"));

        world.Index.ShouldNotBeNull().Resolve("Herald", null, null, null, found);
        found.ShouldHaveSingleItem().Node.ShouldBeSameAs(node);
        found.Clear();
        world.Index.Resolve("Speaker", null, null, null, found);
        found.ShouldBeEmpty();

        world.Deactivate();

        node.Name.ShouldBe("Speaker");
    }

    [Theory]
    [InlineData("", "a name cannot be empty")]
    [InlineData(" \t ", "a name cannot be empty")]
    [InlineData("two\nlines", "a name cannot hold a line break or any other control character")]
    [InlineData("nul\0inside", "a name cannot hold a line break or any other control character")]
    public void A_name_a_node_cannot_have_is_refused_with_its_reason(string name, string reason)
    {
        NodeName.WhyRefused(name).ShouldBe(reason);
    }

    [Fact]
    public void A_name_is_at_most_a_hundred_characters_long()
    {
        NodeName.WhyRefused(new string('x', NodeName.MaxLength)).ShouldBeNull();
        NodeName.WhyRefused(new string('x', NodeName.MaxLength + 1))
            .ShouldBe("a name is at most 100 characters long, and this one has 101");
        NodeName.WhyRefused("Tür 2 (links)").ShouldBeNull();
    }

    [Fact]
    public void A_member_value_prints_the_same_on_every_machine_and_compares_by_what_it_holds()
    {
        MemberValue.Of(new Vector3(0.1f, -0f, 1e20f)).ToString().ShouldBe("0.1, -0, 1E+20");
        MemberValue.Of("Door").ToString().ShouldBe("Door");
        MemberValue.Of(new CFrame(1f, 2f, 3f)).ToString().ShouldBe("1, 2, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1");
        MemberValue.None.ToString().ShouldBe("nil");

        MemberValue.Of("Door").ShouldBe(MemberValue.Of("Door"));
        MemberValue.Of("Door").ShouldNotBe(MemberValue.Of("door"));
        MemberValue.Of(Vector3.One).ShouldNotBe(MemberValue.Of(new CFrame(Vector3.One)));
        MemberValue.Of(new CFrame(Vector3.One)).ShouldBe(MemberValue.Of(new CFrame(1f, 1f, 1f)));
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
