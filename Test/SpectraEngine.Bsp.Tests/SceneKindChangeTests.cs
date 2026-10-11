using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A node that becomes something else says so, and the change that crosses to
/// a UI thread carries its entity class and its kind of light.
/// </summary>
public sealed class SceneKindChangeTests
{
    private static (Scene Scene, SceneChangeLog Log) Observed()
    {
        var scene = new Scene("Kinds");
        var log = new SceneChangeLog();
        log.Observe(scene);
        log.Drain();
        return (scene, log);
    }

    private static Brush Box(float half = 1f) => Brush.CreateBox(new Vector3(-half), new Vector3(half));

    // The one change logged since the last drain.
    private static SceneChange Only(SceneChangeLog log) => log.Drain().Changes.ShouldHaveSingleItem();

    [Fact]
    public void An_added_node_carries_its_entity_class_and_its_kind_of_light()
    {
        (Scene scene, SceneChangeLog log) = Observed();

        scene.Root.AddChild(
            new SceneNode("Door") { BrushKind = BrushKind.Part, Brush = Box(), Entity = new EntityData("func_door") });
        scene.Root.AddChild(new SceneNode("Sun") { Light = new Light { Kind = LightKind.Directional } });
        scene.Root.AddChild(new SceneNode("Crate") { Brush = Box() });

        IReadOnlyList<SceneChange> changes = log.Drain().Changes;

        changes.Count.ShouldBe(3);
        changes[0].NodeKind.ShouldBe(SceneNodeKind.BrushPart);
        changes[0].EntityClass.ShouldBe("func_door");
        changes[0].LightKind.ShouldBeNull();
        changes[1].NodeKind.ShouldBe(SceneNodeKind.Light);
        changes[1].EntityClass.ShouldBeNull();
        changes[1].LightKind.ShouldBe(LightKind.Directional);
        changes[2].EntityClass.ShouldBeNull();
        changes[2].LightKind.ShouldBeNull();
    }

    [Fact]
    public void Giving_a_node_an_entity_and_taking_it_off_each_say_what_the_node_is_now()
    {
        (Scene scene, SceneChangeLog log) = Observed();
        SceneNode slab = scene.Root.AddChild(new SceneNode("Slab") { BrushKind = BrushKind.Part, Brush = Box() });
        log.Drain();

        slab.Entity = new EntityData("func_door");
        SceneChange made = Only(log);
        made.Kind.ShouldBe(SceneChangeKind.KindChanged);
        made.NodeId.ShouldBe(slab.Id);
        made.Name.ShouldBe("Slab");
        made.EntityClass.ShouldBe("func_door");
        made.NodeKind.ShouldBe(SceneNodeKind.BrushPart);

        slab.Entity = null;
        Only(log).EntityClass.ShouldBeNull();
    }

    [Fact]
    public void An_entity_with_no_class_yet_is_told_apart_from_no_entity()
    {
        (Scene scene, SceneChangeLog log) = Observed();
        SceneNode node = scene.Root.AddChild(new SceneNode("Blank"));
        log.Drain();

        node.Entity = new EntityData();

        Only(log).EntityClass.ShouldBe("");
    }

    [Fact]
    public void An_entity_of_another_class_says_so_and_the_same_class_again_says_nothing()
    {
        (Scene scene, SceneChangeLog log) = Observed();
        SceneNode node = scene.Root.AddChild(new SceneNode("Relay") { Entity = new EntityData("logic_relay") });
        log.Drain();

        // What a keyvalue edit does: new data, same class.
        node.Entity = new EntityData("logic_relay");
        log.Drain().Changes.ShouldBeEmpty();

        node.Entity = new EntityData("logic_timer");
        Only(log).EntityClass.ShouldBe("logic_timer");
    }

    [Fact]
    public void A_block_that_becomes_a_part_says_so()
    {
        (Scene scene, SceneChangeLog log) = Observed();
        SceneNode block = scene.Root.AddChild(new SceneNode("Block") { Brush = Box() });
        log.Drain();

        block.BrushKind = BrushKind.Part;

        SceneChange change = Only(log);
        change.Kind.ShouldBe(SceneChangeKind.KindChanged);
        change.NodeKind.ShouldBe(SceneNodeKind.BrushPart);
    }

    [Fact]
    public void A_resize_swaps_the_brush_and_says_nothing()
    {
        // A drag does this every frame. Logged, it would fill the log.
        (Scene scene, SceneChangeLog log) = Observed();
        SceneNode block = scene.Root.AddChild(new SceneNode("Block") { Brush = Box() });
        log.Drain();

        for (int i = 2; i < 40; i++)
            block.Brush = Box(i);

        log.Drain().Changes.ShouldBeEmpty();
    }

    [Fact]
    public void A_brush_that_arrives_leaves_or_turns_into_a_cut_says_so()
    {
        (Scene scene, SceneChangeLog log) = Observed();
        SceneNode node = scene.Root.AddChild(new SceneNode("Node"));
        log.Drain();

        node.Brush = Box();
        Only(log).NodeKind.ShouldBe(SceneNodeKind.BrushWorld);

        node.Brush = Box().WithOperation(BrushOperation.Subtractive);
        Only(log).NodeKind.ShouldBe(SceneNodeKind.BrushSubtractive);

        node.Brush = null;
        Only(log).NodeKind.ShouldBe(SceneNodeKind.Empty);
    }

    [Fact]
    public void A_light_that_arrives_or_leaves_says_so()
    {
        (Scene scene, SceneChangeLog log) = Observed();
        SceneNode node = scene.Root.AddChild(new SceneNode("Lamp"));
        log.Drain();

        node.Light = new Light { Kind = LightKind.Spot };
        SceneChange lit = Only(log);
        lit.NodeKind.ShouldBe(SceneNodeKind.Light);
        lit.LightKind.ShouldBe(LightKind.Spot);

        node.Light = null;
        Only(log).LightKind.ShouldBeNull();
    }

    [Fact]
    public void A_light_edited_in_place_is_reported_by_whoever_changed_its_kind()
    {
        (Scene scene, SceneChangeLog log) = Observed();
        SceneNode node = scene.Root.AddChild(new SceneNode("Lamp") { Light = new Light { Kind = LightKind.Point } });
        log.Drain();

        // No setter on the node runs for this.
        node.Light.ShouldNotBeNull().Kind = LightKind.Rect;
        log.Drain().Changes.ShouldBeEmpty();

        scene.NotifyNodeKindChanged(node);
        Only(log).LightKind.ShouldBe(LightKind.Rect);
    }

    [Fact]
    public void A_node_outside_the_scene_is_not_reported()
    {
        (Scene scene, SceneChangeLog log) = Observed();
        var loose = new SceneNode("Loose") { Entity = new EntityData("logic_relay") };

        loose.Entity = new EntityData("logic_timer");
        loose.Light = new Light();
        scene.NotifyNodeKindChanged(loose);

        log.Drain().Changes.ShouldBeEmpty();
    }

    [Fact]
    public void One_change_raises_the_scenes_event_once()
    {
        (Scene scene, SceneChangeLog log) = Observed();
        SceneNode node = scene.Root.AddChild(new SceneNode("Prop"));
        log.Drain();
        var raised = new List<SceneNode>();
        scene.NodeKindChanged += raised.Add;

        node.Entity = new EntityData("prop_static");

        raised.ShouldBe([node]);
        Only(log).EntityClass.ShouldBe("prop_static");
    }
}
