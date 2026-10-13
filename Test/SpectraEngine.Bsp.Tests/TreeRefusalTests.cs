using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a script may not do to a level's tree, and the words it reads when
/// it tries. A refused change changes nothing.
/// </summary>
public sealed class TreeRefusalTests
{
    private const string OnlyParts = "a world brush sits at or below it, and only parts can {0} while a level plays. " +
        "Convert the brush to a part.";

    private readonly TreeLevel _level = new();

    [Fact]
    public void The_root_takes_no_parent_and_is_neither_cloned_nor_destroyed()
    {
        TreeWrites tree = _level.Play();
        SceneNode root = _level.Scene.Root;

        TreeLevel.Refused(tree, root, _level.Group)
            .ShouldBe("The Parent property of Root is locked: it is the root of the level");
        TreeLevel.Refused(tree, root, null)
            .ShouldBe("The Parent property of Root is locked: it is the root of the level");
        tree.TryClone(root, _level.Copies, out _, out string? cloned).ShouldBeFalse();
        tree.TryDestroy(root, _level.Destroyed, out string? destroyed).ShouldBeFalse();

        cloned.ShouldBe("Root is the root of the level and cannot be cloned");
        destroyed.ShouldBe("Root is the root of the level and cannot be destroyed");
        tree.IsInLevel(root).ShouldBeTrue();
    }

    [Fact]
    public void A_node_cannot_go_under_itself_or_under_what_is_below_it()
    {
        TreeWrites tree = _level.Play();

        TreeLevel.Refused(tree, _level.Group, _level.Group)
            .ShouldBe("Attempt to set parent of Group to Group would result in circular reference");
        TreeLevel.Refused(tree, _level.Group, _level.Crate)
            .ShouldBe("Attempt to set parent of Group to Crate would result in circular reference");
    }

    [Fact]
    public void A_destroyed_node_takes_no_parent_and_is_none()
    {
        TreeWrites tree = _level.Play();
        _level.Destroy(tree, _level.Crate);

        TreeLevel.Refused(tree, _level.Crate, _level.Group)
            .ShouldBe("The Parent property of Crate is locked: Crate was destroyed");
        TreeLevel.Refused(tree, _level.Crate, null)
            .ShouldBe("The Parent property of Crate is locked: Crate was destroyed");
        TreeLevel.Refused(tree, _level.Barrel, _level.Crate)
            .ShouldBe("Barrel cannot get Crate as its parent: Crate was destroyed");
    }

    [Fact]
    public void A_world_brush_is_not_reparented_cloned_or_destroyed_and_neither_is_a_node_above_one()
    {
        SceneNode house = _level.Scene.Root.CreateChild("House");
        EntityMotion.BrushNode(house, "Floor", BrushKind.World, Vector3.Zero);
        TreeWrites tree = _level.Play();

        foreach (SceneNode node in new[] { _level.Wall, house })
        {
            TreeLevel.Refused(tree, node, _level.Group)
                .ShouldBe($"{node.Name} cannot get another parent: {string.Format(OnlyParts, "move")}");
            TreeLevel.Refused(tree, node, null)
                .ShouldBe($"{node.Name} cannot get another parent: {string.Format(OnlyParts, "move")}");
            tree.TryClone(node, _level.Copies, out _, out string? cloned).ShouldBeFalse();
            tree.TryDestroy(node, _level.Destroyed, out string? destroyed).ShouldBeFalse();

            cloned.ShouldBe($"{node.Name} cannot be cloned: {string.Format(OnlyParts, "be made")}");
            destroyed.ShouldBe($"{node.Name} cannot be destroyed: {string.Format(OnlyParts, "be destroyed")}");
            tree.IsInLevel(node).ShouldBeTrue();
            TreeWrites.IsDestroyed(node).ShouldBeFalse();
        }

        tree.MadeCount.ShouldBe(0);
        _level.World.Journal.Tree.TouchedParentCount.ShouldBe(0);
    }

    [Fact]
    public void A_part_goes_under_a_world_brush_which_stays_where_it_is()
    {
        TreeWrites tree = _level.Play();
        CFrame before = _level.Crate.CFrame;

        TreeLevel.Parent(tree, _level.Crate, _level.Wall);

        _level.Wall.Children.ShouldBe([_level.Crate]);
        _level.Crate.CFrame.FuzzyEq(before).ShouldBeTrue();
        _level.Crate.BrushKind.ShouldBe(BrushKind.Part);
        _level.Wall.Parent.ShouldBeSameAs(_level.Scene.Root);
    }

    [Fact]
    public void A_node_a_bake_took_a_world_brush_from_is_refused_in_the_same_words()
    {
        var baked = new SceneNode("Floor") { BakedKind = SceneNodeKind.BrushWorld };
        _level.Scene.Root.AddChild(baked);
        TreeWrites tree = _level.Play();

        TreeLevel.Refused(tree, baked, _level.Group)
            .ShouldBe($"Floor cannot get another parent: {string.Format(OnlyParts, "move")}");
        tree.TryClone(baked, _level.Copies, out _, out string? cloned).ShouldBeFalse();
        tree.TryDestroy(baked, _level.Destroyed, out string? destroyed).ShouldBeFalse();

        cloned.ShouldBe($"Floor cannot be cloned: {string.Format(OnlyParts, "be made")}");
        destroyed.ShouldBe($"Floor cannot be destroyed: {string.Format(OnlyParts, "be destroyed")}");
    }

    [Fact]
    public void A_brush_does_not_go_under_a_scaled_node_and_a_light_does()
    {
        SceneNode big = _level.Scene.Root.CreateChild("Big");
        big.LocalScale = new Vector3(2f, 2f, 2f);
        SceneNode inside = big.CreateChild("Inside");
        TreeWrites tree = _level.Play();
        Vector3 lampAt = _level.Lamp.WorldPosition;

        TreeLevel.Refused(tree, _level.Crate, inside).ShouldBe(
            "Crate cannot get Inside as its parent: that node or one above it is scaled, and a node cannot " +
            "scale a brush at or below it. Put it under a node that is not scaled.");
        TreeLevel.Refused(tree, _level.Group, big).ShouldStartWith("Group cannot get Big as its parent: that node");
        TreeLevel.Parent(tree, _level.Lamp, inside);

        Vector3.Distance(_level.Lamp.WorldPosition, lampAt).ShouldBeLessThan(1e-4f);
    }

    [Fact]
    public void Nothing_is_made_cloned_moved_or_destroyed_once_the_level_has_stopped()
    {
        TreeWrites tree = _level.Play();
        _level.World.Deactivate();

        tree.TryMake("Part", out _, out string? made).ShouldBeFalse();
        tree.TryClone(_level.Crate, _level.Copies, out _, out string? cloned).ShouldBeFalse();
        tree.TryDestroy(_level.Crate, _level.Destroyed, out string? destroyed).ShouldBeFalse();

        made.ShouldBe("Instance.new made nothing: the level has stopped");
        cloned.ShouldBe("Crate was not cloned: the level has stopped");
        destroyed.ShouldBe("Crate cannot be changed: the level has stopped.");
        TreeLevel.Refused(tree, _level.Crate, null).ShouldBe("Crate cannot be changed: the level has stopped.");
        tree.MadeCount.ShouldBe(0);
    }

    [Fact]
    public void A_refused_parent_reaches_a_script_through_the_door_of_every_other_member()
    {
        TreeWrites tree = _level.Play();
        var writes = new ScriptWrites(_level.World);

        writes.TryWrite(_level.Wall, SettableMemberId.Parent, MemberValue.OfNode(null), out string? locked).ShouldBeFalse();
        writes.TryWrite(_level.Crate, SettableMemberId.Parent, MemberValue.Of("Lamp"), out string? wrong).ShouldBeFalse();
        writes.TryWrite(_level.Crate, SettableMemberId.Parent, MemberValue.OfNode(_level.Lamp), out string? taken)
            .ShouldBeTrue(taken);

        locked.ShouldBe($"Wall cannot get another parent: {string.Format(OnlyParts, "move")}");
        wrong.ShouldBe("Unable to assign property Parent. Instance expected, got string");
        ScriptWrites.Read(_level.Crate, SettableMemberId.Parent).ShouldBe(MemberValue.OfNode(_level.Lamp));
        ScriptWrites.Read(_level.Scene.Root, SettableMemberId.Parent).ShouldBe(MemberValue.OfNode(null));
        tree.MadeCount.ShouldBe(0);
    }
}
