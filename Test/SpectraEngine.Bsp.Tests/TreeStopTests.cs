using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Stop puts a level's tree back as it was built: what was made is gone,
/// and what was destroyed or moved is where it was among its siblings, with
/// every value it had. The level then saves to the bytes it saved to before.
/// </summary>
public sealed class TreeStopTests
{
    private readonly TreeLevel _level = new();

    [Fact]
    public void Stop_takes_away_what_was_made_and_puts_back_what_was_destroyed_and_moved()
    {
        byte[] before = _level.Saved();
        TreeWrites tree = _level.Play();

        SceneNode spawned = _level.Make(tree, "Part");
        TreeLevel.Parent(tree, spawned, _level.Scene.Root);
        TreeLevel.Parent(tree, _level.Clone(tree, _level.Group), _level.Lamp);
        TreeLevel.Parent(tree, _level.Barrel, _level.Scene.Root);
        TreeLevel.Parent(tree, _level.Lamp, null);
        _level.Destroy(tree, _level.Crate);
        _level.Saved().ShouldNotBe(before);

        _level.World.Deactivate();

        _level.Saved().ShouldBe(before);
        _level.Scene.Root.Children.ShouldBe([_level.Wall, _level.Group, _level.Lamp, _level.Sign]);
        _level.Group.Children.ShouldBe([_level.Crate, _level.Barrel]);
        _level.Lamp.Children.ShouldBeEmpty();
        TreeWrites.IsDestroyed(_level.Crate).ShouldBeFalse();
        spawned.Parent.ShouldBeNull();
        _level.World.Journal.Tree.TouchedParentCount.ShouldBe(0);
        _level.World.Journal.Tree.MadeCount.ShouldBe(0);
    }

    [Fact]
    public void A_node_that_went_under_its_own_child_is_back_above_it()
    {
        SceneNode outer = _level.Scene.Root.CreateChild("Outer");
        SceneNode inner = outer.CreateChild("Inner");
        byte[] before = _level.Saved();
        TreeWrites tree = _level.Play();

        TreeLevel.Parent(tree, inner, _level.Scene.Root);
        TreeLevel.Parent(tree, outer, inner);

        _level.World.Deactivate();

        inner.Parent.ShouldBeSameAs(outer);
        outer.Parent.ShouldBeSameAs(_level.Scene.Root);
        _level.Saved().ShouldBe(before);
    }

    [Fact]
    public void Children_are_back_in_their_order_whatever_order_a_level_worked_in()
    {
        SceneNode[] pegs = [.. Enumerable.Range(0, 6).Select(i => _level.Group.CreateChild($"Peg{i}"))];
        SceneNode[] built = [.. _level.Group.Children];
        byte[] before = _level.Saved();
        TreeWrites tree = _level.Play();

        // Out and in again in another order, with made nodes between them.
        foreach (int at in new[] { 4, 0, 5, 2 })
            TreeLevel.Parent(tree, pegs[at], _level.Lamp);
        TreeLevel.Parent(tree, _level.Make(tree, "Folder"), _level.Group);
        foreach (int at in new[] { 2, 4, 0 })
            TreeLevel.Parent(tree, pegs[at], _level.Group);
        TreeLevel.Parent(tree, _level.Make(tree, "Part"), _level.Group);
        _level.Destroy(tree, pegs[1]);
        _level.Group.Children.ShouldNotBe(built);

        _level.World.Deactivate();

        _level.Group.Children.ShouldBe(built);
        _level.Saved().ShouldBe(before);
    }

    [Fact]
    public void A_node_of_the_level_under_a_made_node_that_was_destroyed_comes_home()
    {
        byte[] before = _level.Saved();
        TreeWrites tree = _level.Play();
        SceneNode box = _level.Make(tree, "Folder");
        TreeLevel.Parent(tree, box, _level.Scene.Root);
        TreeLevel.Parent(tree, _level.Crate, box);
        _level.Destroy(tree, box);
        TreeWrites.IsDestroyed(_level.Crate).ShouldBeTrue();

        _level.World.Deactivate();

        _level.Group.Children.ShouldBe([_level.Crate, _level.Barrel]);
        TreeWrites.IsDestroyed(_level.Crate).ShouldBeFalse();
        _level.Saved().ShouldBe(before);
    }

    [Fact]
    public void A_node_an_entity_moved_and_a_script_then_destroyed_is_back_where_it_was_built()
    {
        SceneNode door = EntityMotion.Slider(
            EntityMotion.BrushNode(_level.Group, "Door", BrushKind.Part, new Vector3(0f, 0.5f, -0f)),
            new Vector3(0f, 2f, 0f), ticks: 20);
        byte[] authored = EntityMotion.Bits(door.LocalTransform);
        byte[] before = _level.Saved();
        TreeWrites tree = _level.Play();
        for (int i = 0; i < 7; i++)
            _level.Tick();

        EntityMotion.Bits(door.LocalTransform).ShouldNotBe(authored);
        _level.Destroy(tree, door);
        for (int i = 0; i < 3; i++)
            _level.Tick();

        _level.World.Deactivate();

        door.Parent.ShouldBeSameAs(_level.Group);
        EntityMotion.Bits(door.LocalTransform).ShouldBe(authored);
        _level.Saved().ShouldBe(before);
    }

    [Fact]
    public void A_stop_that_throws_still_puts_the_tree_back()
    {
        var level = new TreeLevel(FailingRemovalEntity.AddTo(EntityMotion.Catalog([])));
        EntityRuntime.Place(level.Scene.Root, "broken", FailingRemovalEntity.Class);
        byte[] before = level.Saved();
        TreeWrites tree = level.Play();
        TreeLevel.Parent(tree, level.Make(tree, "Part"), level.Group);
        TreeLevel.Parent(tree, level.Barrel, level.Lamp);
        level.Destroy(tree, level.Crate);
        new ScriptWrites(level.World).TryWrite(level.Lamp, SettableMemberId.Name, MemberValue.Of("Torch"), out _)
            .ShouldBeTrue();

        Should.Throw<InvalidOperationException>(level.World.Deactivate);

        level.Saved().ShouldBe(before);
        level.Group.Children.ShouldBe([level.Crate, level.Barrel]);
        level.World.Journal.Tree.TouchedParentCount.ShouldBe(0);
    }

    [Fact]
    public void A_value_that_cannot_go_back_does_not_keep_the_tree_from_going_back()
    {
        TreeWrites tree = _level.Play();
        TreeLevel.Parent(tree, _level.Barrel, _level.Lamp);
        _level.World.Journal.Record(_level.Lamp, new JournalKind<string>(
            static node => node.Name, static (_, _) => throw new InvalidOperationException("It will not go back.")));

        Should.Throw<InvalidOperationException>(_level.World.Deactivate).Message.ShouldBe("It will not go back.");

        _level.Group.Children.ShouldBe([_level.Crate, _level.Barrel]);
    }

    [Fact]
    public void What_a_level_changes_on_a_node_it_made_is_not_kept_for_Stop()
    {
        TreeWrites tree = _level.Play();
        var writes = new ScriptWrites(_level.World);
        SceneNode part = _level.Make(tree, "Part");
        SceneNode lamp = _level.Make(tree, "PointLight");
        TreeLevel.Parent(tree, part, _level.Group);
        TreeLevel.Parent(tree, lamp, part);

        Write(writes, part, SettableMemberId.Position, MemberValue.Of(new Vector3(1f, 2f, 3f)));
        Write(writes, part, SettableMemberId.Name, MemberValue.Of("Spawned"));
        Write(writes, part, SettableMemberId.CanCollide, MemberValue.Of(false));
        Write(writes, part, SettableMemberId.Visible, MemberValue.Of(false));
        Write(writes, lamp, SettableMemberId.Brightness, MemberValue.Of(3d));
        Write(writes, lamp, SettableMemberId.Enabled, MemberValue.Of(false));
        TreeLevel.Parent(tree, part, _level.Scene.Root);

        _level.World.Journal.Count.ShouldBe(0);
        _level.World.Journal.Tree.TouchedParentCount.ShouldBe(2);
        part.Name.ShouldBe("Spawned");
    }

    [Fact]
    public void A_parent_is_recorded_once_however_often_its_children_change()
    {
        TreeWrites tree = _level.Play();
        for (int i = 0; i < 50; i++)
        {
            SceneNode part = _level.Make(tree, "Part");
            TreeLevel.Parent(tree, part, _level.Group);
            if (i % 2 == 0)
                _level.Destroy(tree, part);
        }

        _level.World.Journal.Tree.TouchedParentCount.ShouldBe(1);
        tree.MadeCount.ShouldBe(25);

        _level.World.Deactivate();

        _level.Group.Children.ShouldBe([_level.Crate, _level.Barrel]);
    }

    private static void Write(ScriptWrites writes, SceneNode node, SettableMemberId member, MemberValue value) =>
        writes.TryWrite(node, member, in value, out string? refusal).ShouldBeTrue(refusal);
}
