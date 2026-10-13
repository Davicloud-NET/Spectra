using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a wiring view is told about a level's scripts: the ones that will
/// run, and the entity each is inside.
/// </summary>
public sealed class LogicGraphInfoScriptTests
{
    private readonly Scene _scene = new("Scripted");

    [Fact]
    public void A_script_names_the_nearest_entity_above_it()
    {
        SceneNode button = Entity(_scene.Root, "KeyButton");
        SceneNode cap = button.CreateChild("Cap");
        SceneNode beep = Script(cap, "Beep");
        SceneNode main = Script(_scene.Root, "Main");

        LogicGraphInfo info = LogicGraphInfo.Capture(_scene);

        info.Scripts.ShouldBe(
        [
            new LogicScriptInfo(beep.Id, "Beep", button.Id),
            new LogicScriptInfo(main.Id, "Main", Guid.Empty),
        ]);
    }

    [Fact]
    public void A_script_on_the_node_of_an_entity_is_inside_that_entity()
    {
        SceneNode door = Entity(_scene.Root, "Door7");
        door.Script = new Script("Scripts/door.luau");

        LogicGraphInfo info = LogicGraphInfo.Capture(_scene);

        info.Entities.ShouldHaveSingleItem().NodeId.ShouldBe(door.Id);
        info.Scripts.ShouldBe([new LogicScriptInfo(door.Id, "Door7", door.Id)]);
    }

    [Fact]
    public void A_script_that_is_switched_off_and_a_module_are_not_listed()
    {
        SceneNode button = Entity(_scene.Root, "KeyButton");
        Script(button, "Off").Script = new Script("Scripts/off.luau") { IsEnabled = false };
        Script(button, "Shared").Script = new Script("Scripts/shared.luau") { IsModule = true };

        LogicGraphInfo.Capture(_scene).Scripts.ShouldBeEmpty();
    }

    [Fact]
    public void The_same_capture_comes_back_until_a_script_changes()
    {
        SceneNode button = Entity(_scene.Root, "KeyButton");
        SceneNode beep = Script(button, "Beep");
        LogicGraphInfo first = LogicGraphInfo.Capture(_scene);

        LogicGraphInfo same = LogicGraphInfo.Capture(_scene, first);
        beep.Name = "Click";
        LogicGraphInfo renamed = LogicGraphInfo.Capture(_scene, same);
        beep.Script = new Script("Scripts/beep.luau") { IsEnabled = false };
        LogicGraphInfo switchedOff = LogicGraphInfo.Capture(_scene, renamed);
        Script(_scene.Root, "Main");
        LogicGraphInfo added = LogicGraphInfo.Capture(_scene, switchedOff);

        same.ShouldBeSameAs(first);
        renamed.ShouldNotBeSameAs(first);
        renamed.Scripts.ShouldHaveSingleItem().Name.ShouldBe("Click");
        switchedOff.Scripts.ShouldBeEmpty();
        added.Scripts.ShouldHaveSingleItem().Name.ShouldBe("Main");
    }

    [Fact]
    public void A_script_moved_under_another_entity_is_a_new_capture()
    {
        SceneNode button = Entity(_scene.Root, "KeyButton");
        SceneNode door = Entity(_scene.Root, "Door7");
        SceneNode beep = Script(button, "Beep");
        LogicGraphInfo first = LogicGraphInfo.Capture(_scene);

        door.AddChild(beep);
        LogicGraphInfo moved = LogicGraphInfo.Capture(_scene, first);

        moved.ShouldNotBeSameAs(first);
        moved.Scripts.ShouldHaveSingleItem().EntityId.ShouldBe(door.Id);
    }

    private static SceneNode Entity(SceneNode parent, string name)
    {
        SceneNode node = parent.CreateChild(name);
        node.Entity = new EntityData("func_button");
        return node;
    }

    private static SceneNode Script(SceneNode parent, string name)
    {
        SceneNode node = parent.CreateChild(name);
        node.Script = new Script($"Scripts/{name.ToLowerInvariant()}.luau");
        return node;
    }
}
