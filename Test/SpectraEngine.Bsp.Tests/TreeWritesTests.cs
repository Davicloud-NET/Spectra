using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a script adds to a level's tree, takes from it and moves within it:
/// a made node, a clone, a new parent and a destroyed node, each as Roblox
/// means it.
/// </summary>
public sealed class TreeWritesTests
{
    private readonly TreeLevel _level = new();

    [Fact]
    public void A_made_part_is_a_solid_cube_one_unit_to_a_side_in_no_tree()
    {
        TreeWrites tree = _level.Play();

        SceneNode part = _level.Make(tree, "Part");

        part.Name.ShouldBe("Part");
        part.Parent.ShouldBeNull();
        part.BrushKind.ShouldBe(BrushKind.Part);
        part.IsStaticWorldBrush.ShouldBeFalse();
        var box = part.Brush.ShouldNotBeNull().LocalBounds;
        Vector3.Distance(box.Min, new Vector3(-MadeNodes.PartSize / 2f)).ShouldBeLessThan(1e-6f);
        Vector3.Distance(box.Max, new Vector3(MadeNodes.PartSize / 2f)).ShouldBeLessThan(1e-6f);
        MadeNodes.PartSize.ShouldBe(1f);
        (part.CanCollide, part.CanQuery, part.CanTouch, part.IsRendered).ShouldBe((true, true, true, true));
        tree.IsInLevel(part).ShouldBeFalse();
        TreeWrites.IsMade(part).ShouldBeTrue();
        tree.MadeCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("Part")]
    [InlineData("Folder")]
    [InlineData("PointLight")]
    [InlineData("SpotLight")]
    [InlineData("SurfaceLight")]
    [InlineData("DirectionalLight")]
    [InlineData("DiscLight")]
    public void A_made_node_is_of_the_class_it_was_asked_for_and_named_after_it(string className)
    {
        TreeWrites tree = _level.Play();

        SceneNode made = _level.Make(tree, className);

        NodeClassName.Of(made).ShouldBe(className);
        made.Name.ShouldBe(className);
        MadeNodes.Classes.ShouldContain(className);
    }

    [Fact]
    public void A_made_light_is_white_with_a_brightness_of_one_and_Robloxs_range()
    {
        TreeWrites tree = _level.Play();

        Light point = _level.Make(tree, "PointLight").Light.ShouldNotBeNull();
        Light spot = _level.Make(tree, "SpotLight").Light.ShouldNotBeNull();

        (point.Intensity, point.Color, point.Range).ShouldBe((1f, Vector3.One, 8f));
        (spot.Intensity, spot.Color, spot.Range).ShouldBe((1f, Vector3.One, 16f));
        point.Enabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("MeshPart")]
    [InlineData("Script")]
    [InlineData("Model")]
    [InlineData("lifecycle")]
    [InlineData("part")]
    [InlineData("")]
    public void A_class_that_cannot_be_made_from_its_name_is_refused_with_what_can_be(string className)
    {
        TreeWrites tree = _level.Play();

        tree.TryMake(className, out SceneNode? made, out string? refusal).ShouldBeFalse();

        made.ShouldBeNull();
        refusal.ShouldBe(
            $"Unable to create an Instance of type \"{className}\". Instance.new makes a Part, a Folder, " +
            "a PointLight, a SpotLight, a SurfaceLight, a DirectionalLight or a DiscLight. " +
            "Clone a node of the level for anything else");
        tree.MadeCount.ShouldBe(0);
    }

    [Fact]
    public void A_node_keeps_its_place_in_the_world_when_it_gets_another_parent()
    {
        TreeWrites tree = _level.Play();
        CFrame before = _level.Crate.CFrame;

        TreeLevel.Parent(tree, _level.Crate, _level.Lamp);

        _level.Crate.Parent.ShouldBeSameAs(_level.Lamp);
        _level.Lamp.Children.ShouldBe([_level.Crate]);
        _level.Group.Children.ShouldBe([_level.Barrel]);
        _level.Crate.CFrame.FuzzyEq(before).ShouldBeTrue();
        _level.Crate.LocalPosition.ShouldNotBe(new Vector3(1f, 0.5f, 2f));
    }

    [Fact]
    public void A_made_part_lands_where_it_was_put_whatever_it_is_parented_to()
    {
        TreeWrites tree = _level.Play();
        SceneNode part = _level.Make(tree, "Part");
        part.LocalPosition = new Vector3(3f, 2f, 1f);

        TreeLevel.Parent(tree, part, _level.Group);

        tree.IsInLevel(part).ShouldBeTrue();
        Vector3.Distance(part.WorldPosition, new Vector3(3f, 2f, 1f)).ShouldBeLessThan(1e-4f);
        _level.Group.Children[^1].ShouldBeSameAs(part);
    }

    [Fact]
    public void No_parent_takes_a_node_out_of_the_level_where_it_stands()
    {
        TreeWrites tree = _level.Play();
        CFrame before = _level.Crate.CFrame;

        TreeLevel.Parent(tree, _level.Crate, null);

        _level.Crate.Parent.ShouldBeNull();
        tree.IsInLevel(_level.Crate).ShouldBeFalse();
        TreeWrites.IsDestroyed(_level.Crate).ShouldBeFalse();
        _level.Crate.CFrame.FuzzyEq(before).ShouldBeTrue();
        _level.Scene.TryFindById(_level.Crate.Id, out _).ShouldBeFalse();

        TreeLevel.Parent(tree, _level.Crate, _level.Scene.Root);

        tree.IsInLevel(_level.Crate).ShouldBeTrue();
        _level.Crate.CFrame.FuzzyEq(before).ShouldBeTrue();
    }

    [Fact]
    public void The_parent_a_node_has_is_taken_and_changes_nothing()
    {
        TreeWrites tree = _level.Play();
        Transform before = _level.Crate.LocalTransform;

        TreeLevel.Parent(tree, _level.Crate, _level.Group);

        EntityMotion.Bits(_level.Crate.LocalTransform).ShouldBe(EntityMotion.Bits(before));
        _level.Group.Children.ShouldBe([_level.Crate, _level.Barrel]);
        _level.World.Journal.Count.ShouldBe(0);
        _level.World.Journal.Tree.TouchedParentCount.ShouldBe(0);
    }

    [Fact]
    public void A_clone_is_a_copy_in_no_tree_that_stands_where_its_source_stands()
    {
        _level.Crate.CanTouch = false;
        _level.Barrel.CreateChild("Lid").LocalPosition = new Vector3(0f, 0.6f, 0f);
        TreeWrites tree = _level.Play();

        SceneNode copy = _level.Clone(tree, _level.Group);

        copy.ShouldNotBeSameAs(_level.Group);
        copy.Id.ShouldNotBe(_level.Group.Id);
        copy.Parent.ShouldBeNull();
        tree.IsInLevel(copy).ShouldBeFalse();
        copy.Children.Select(child => child.Name).ShouldBe(["Crate", "Barrel"]);
        copy.Children[1].Children.ShouldHaveSingleItem().Name.ShouldBe("Lid");
        copy.Children[0].CanTouch.ShouldBeFalse();
        copy.Children[0].Brush.ShouldNotBeSameAs(_level.Crate.Brush);
        copy.CFrame.FuzzyEq(_level.Group.CFrame).ShouldBeTrue();
        copy.Children[0].CFrame.FuzzyEq(_level.Crate.CFrame).ShouldBeTrue();
        _level.Copies.Count.ShouldBe(4);
        _level.Copies.ShouldAllBe(pair => TreeWrites.IsMade(pair.Copy) && !TreeWrites.IsMade(pair.Source));
        tree.MadeCount.ShouldBe(4);
    }

    [Fact]
    public void A_clone_of_a_node_under_a_moved_parent_reads_the_place_its_source_has_in_the_world()
    {
        TreeWrites tree = _level.Play();

        SceneNode copy = _level.Clone(tree, _level.Crate);

        copy.CFrame.FuzzyEq(_level.Crate.CFrame).ShouldBeTrue();
        TreeLevel.Parent(tree, copy, _level.Group);
        copy.LocalCFrame.FuzzyEq(_level.Crate.LocalCFrame).ShouldBeTrue();
    }

    [Fact]
    public void A_clone_points_a_reference_to_a_copied_node_at_the_copy_and_keeps_one_to_a_node_outside()
    {
        SceneNode rig = _level.Scene.Root.CreateChild("Rig");
        SceneNode button = EntityRuntime.Place(rig, "Button", "recorder");
        SceneNode door = EntityRuntime.Place(rig, "Door", "recorder");
        EntityData wired = button.Entity.ShouldNotBeNull();
        wired.SetValue("opens", door.Id.ToString("D"));
        wired.SetValue("lights", _level.Lamp.Id.ToString("D"));
        wired.SetValue("tag", "kept as it is");
        EntityRuntime.Wire(button, "OnPressed", "Door", "Follow", parameter: door.Id.ToString("D"));
        EntityRuntime.Wire(button, "OnPressed", "Door", "Say", parameter: "open");
        TreeWrites tree = _level.Play();

        SceneNode copy = _level.Clone(tree, rig);

        EntityData copied = copy.Children[0].Entity.ShouldNotBeNull();
        string doorCopy = copy.Children[1].Id.ToString("D");
        copied.Keyvalues.Select(pair => pair.Value).ShouldBe([doorCopy, _level.Lamp.Id.ToString("D"), "kept as it is"]);
        copied.Connections.Select(wire => wire.Parameter).ShouldBe([doorCopy, "open"]);
        wired.Keyvalues[0].Value.ShouldBe(door.Id.ToString("D"));
        wired.Connections[0].Parameter.ShouldBe(door.Id.ToString("D"));
    }

    [Fact]
    public void Destroy_takes_a_node_and_all_below_it_apart_and_out_of_the_level()
    {
        SceneNode lid = _level.Barrel.CreateChild("Lid");
        TreeWrites tree = _level.Play();

        _level.Destroy(tree, _level.Group);

        _level.Destroyed.ShouldBe([_level.Group, _level.Crate, _level.Barrel, lid]);
        foreach (SceneNode gone in _level.Destroyed)
        {
            gone.Parent.ShouldBeNull();
            gone.Children.ShouldBeEmpty();
            TreeWrites.IsDestroyed(gone).ShouldBeTrue();
            tree.IsInLevel(gone).ShouldBeFalse();
        }

        _level.Scene.Root.Children.ShouldNotContain(_level.Group);
    }

    [Fact]
    public void A_destroyed_node_still_reads_what_it_held_and_can_be_destroyed_again()
    {
        TreeWrites tree = _level.Play();
        CFrame before = _level.Crate.CFrame;

        _level.Destroy(tree, _level.Crate);
        _level.Destroy(tree, _level.Crate);

        _level.Destroyed.ShouldBeEmpty();
        _level.Crate.Name.ShouldBe("Crate");
        _level.Crate.Brush.ShouldNotBeNull();
        _level.Crate.CFrame.FuzzyEq(before).ShouldBeTrue();
    }

    [Fact]
    public void A_made_node_that_is_destroyed_no_longer_counts()
    {
        TreeWrites tree = _level.Play();
        SceneNode folder = _level.Make(tree, "Folder");
        TreeLevel.Parent(tree, _level.Make(tree, "Part"), folder);
        TreeLevel.Parent(tree, folder, _level.Scene.Root);
        tree.MadeCount.ShouldBe(2);

        _level.Destroy(tree, folder);

        tree.MadeCount.ShouldBe(0);
        _level.World.Journal.Tree.DestroyedCount.ShouldBe(0);
    }
}
