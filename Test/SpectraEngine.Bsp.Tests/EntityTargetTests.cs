using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Which entities a wire can aim at. The picker's list and the unknown-target
/// warning come from the same walk.
/// </summary>
public sealed class EntityTargetTests
{
    private static SceneNode Entity(SceneNode parent, string name, string className)
    {
        SceneNode node = parent.CreateChild(name);
        node.Entity = new EntityData(className);
        return node;
    }

    private static List<EntityTargetInfo> Targets(Scene scene, out bool truncated)
    {
        List<EntityTargetInfo> into = [];
        EntityPanelInfo.CollectTargets(scene, into, out truncated);
        return into;
    }

    [Fact]
    public void Targets_carry_a_name_and_a_class_in_walk_order()
    {
        var scene = new Scene("Test");
        Entity(scene.Root, "timer", "logic_timer");
        SceneNode group = scene.Root.CreateChild("group");
        Entity(group, "relay", "logic_relay");

        List<EntityTargetInfo> targets = Targets(scene, out bool truncated);

        truncated.ShouldBeFalse();
        targets.Count.ShouldBe(2);
        targets[0].Name.ShouldBe("timer");
        targets[0].ClassName.ShouldBe("logic_timer");
        targets[1].Name.ShouldBe("relay");
    }

    [Fact]
    public void A_plain_node_is_not_a_target()
    {
        var scene = new Scene("Test");
        scene.Root.CreateChild("wall");
        Entity(scene.Root, "relay", "logic_relay");

        // The runtime resolves entities only, so a wire aimed at a brush node
        // named "door" delivers to nothing.
        List<EntityTargetInfo> targets = Targets(scene, out _);
        targets.Count.ShouldBe(1);
        targets[0].Name.ShouldBe("relay");
    }

    [Fact]
    public void The_list_is_capped_and_says_so()
    {
        var scene = new Scene("Test");
        for (int i = 0; i < EntityPanelInfo.MaxTargets + 10; i++)
            Entity(scene.Root, "e" + i, "logic_relay");

        List<EntityTargetInfo> targets = Targets(scene, out bool truncated);

        targets.Count.ShouldBe(EntityPanelInfo.MaxTargets);
        truncated.ShouldBeTrue();
    }

    [Fact]
    public void A_capture_publishes_the_targets_and_the_resolve_check_agrees_with_them()
    {
        var scene = new Scene("Test");
        SceneNode timer = Entity(scene.Root, "timer", "logic_timer");
        Entity(scene.Root, "relay", "logic_relay");

        timer.Entity!.Connections.Add(
            new EntityConnection("OnTimer", "relay", "Trigger", "", 0f, EntityConnection.Infinite));
        timer.Entity!.Connections.Add(
            new EntityConnection("OnTimer", "nobody", "Trigger", "", 0f, EntityConnection.Infinite));

        EntityPanelInfo info = EntityPanelInfo.Capture(timer, null, scene)!;

        info.Targets.Count.ShouldBe(2);

        info.Connections[0].TargetResolves.ShouldBeTrue();
        info.Connections[1].TargetResolves.ShouldBeFalse();
    }

    [Fact]
    public void An_entity_with_no_wiring_still_publishes_the_targets()
    {
        var scene = new Scene("Test");
        SceneNode timer = Entity(scene.Root, "timer", "logic_timer");
        Entity(scene.Root, "relay", "logic_relay");

        EntityPanelInfo info = EntityPanelInfo.Capture(timer, null, scene)!;

        // The picker is needed most when adding the first wire.
        info.Connections.Count.ShouldBe(0);
        info.Targets.Count.ShouldBe(2);
    }

    [Fact]
    public void A_capture_with_no_scene_publishes_no_targets()
    {
        var node = new SceneNode("timer") { Entity = new EntityData("logic_timer") };

        EntityPanelInfo info = EntityPanelInfo.Capture(node, null, null)!;

        info.Targets.Count.ShouldBe(0);
        info.TargetsTruncated.ShouldBeFalse();
    }
}
