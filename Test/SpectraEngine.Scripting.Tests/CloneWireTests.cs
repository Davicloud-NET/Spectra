using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Entities;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A clone keeps the names of its source, and a wire finds its target by
/// name. A wire inside a clone reaches the entity that was copied with it.
/// Any other wire reaches every entity of the name, the clones too.
/// </summary>
public sealed class CloneWireTests
{
    // Hears which of the two pairs was reached, since both have one name.
    private const string Listen = """
        local pair = workspace.Pair
        local copy = pair:Clone()
        copy.Parent = workspace
        pair.Hearer.OnPing:Connect(function() print("the source heard") end)
        copy.Hearer.OnPing:Connect(function() print("the copy heard") end)
        task.wait()
        """;

    [Fact]
    public void A_wire_of_a_clone_that_names_an_entity_of_the_clone_reaches_that_one_alone()
    {
        using var level = new ScriptLevel();
        Pair(level);
        level.AddScript(level.Scene.Root, Listen + "copy.Caller:Ping()", "Main");

        level.Play().Tick(6);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Caller got Ping ''", "Hearer got Ping ''", "the copy heard"]);
    }

    [Fact]
    public void A_wire_of_the_level_reaches_every_entity_of_the_name_and_the_clone_with_them()
    {
        using var level = new ScriptLevel();
        Pair(level);
        level.AddScript(level.Scene.Root, Listen + "pair.Caller:Ping()", "Main");

        level.Play().Tick(6);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "Caller got Ping ''", "Hearer got Ping ''", "Hearer got Ping ''", "the source heard", "the copy heard",
        ]);
    }

    [Fact]
    public void A_wire_of_a_clone_that_names_nothing_of_the_clone_reaches_the_level()
    {
        using var level = new ScriptLevel();
        Pair(level);
        level.AddScript(level.Scene.Root, """
            local pair = workspace.Pair
            local caller = pair.Caller:Clone()
            caller.Parent = workspace
            pair.Hearer.OnPing:Connect(function() print("the source heard") end)
            task.wait()
            caller:Ping()
            """, "Main");

        level.Play().Tick(6);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Caller got Ping ''", "Hearer got Ping ''", "the source heard"]);
    }

    [Fact]
    public void A_gate_cloned_twice_with_the_wire_from_its_button_to_its_door_opens_one_door_on_one_press()
    {
        using var level = new ScriptLevel();
        SceneNode gate = Gate(level);
        level.AddScript(level.Scene.Root, """
            local gate = workspace.Gate
            for i = 1, 2 do
                local copy = gate:Clone()
                copy.Name = "Gate" .. i
                copy.Position += Vector3.new(0, 0, 10 * i)
                copy.Parent = workspace
            end
            task.wait()
            workspace.Gate2.Button:Press()
            """, "Builder");
        var before = new LevelState(level.Scene);

        level.Play().Tick(40);

        level.Errors.ShouldBeEmpty();
        Door(level, "Gate2").IsFullyOpen.ShouldBeTrue();
        Door(level, "Gate1").TicksTravelled.ShouldBe(0);
        Door(level, gate.Name).TicksTravelled.ShouldBe(0);

        level.Stop();

        new LevelState(level.Scene).ShouldBe(before, "after the gates");
    }

    // Two entities under one node: Caller's wire sends Hearer what it got.
    private static void Pair(ScriptLevel level)
    {
        SceneNode pair = level.Scene.Root.CreateChild("Pair");
        var caller = new EntityData(EchoEntity.Class);
        caller.Connections.Add(new EntityConnection("OnPing", "Hearer", "Ping", "", 0f, EntityConnection.Infinite));
        pair.CreateChild("Caller").Entity = caller;
        pair.CreateChild("Hearer").Entity = new EntityData(EchoEntity.Class);
    }

    // A button and a door under one node, and the wire that opens the door
    // when the button is pressed. Away from where the player starts.
    private static SceneNode Gate(ScriptLevel level)
    {
        SceneNode gate = level.Scene.Root.CreateChild("Gate");
        gate.LocalPosition = new Vector3(20f, 0f, 20f);

        var button = new EntityData("func_button");
        button.SetValue("distance", "0.1");
        button.SetValue("speed", "6");
        button.SetValue("wait", "0");
        button.Connections.Add(new EntityConnection("OnPressed", "Door", "Open", "", 0f, EntityConnection.Infinite));
        level.Part("Button", new Vector3(0f, 1f, 0f), gate).Entity = button;

        var door = new EntityData("func_door");
        door.SetValue("distance", "1");
        door.SetValue("speed", "6");
        door.SetValue("wait", "-1");
        level.Part("Door", new Vector3(4f, 1f, 0f), gate).Entity = door;
        return gate;
    }

    private static FuncDoor Door(ScriptLevel level, string gate) =>
        level.Entities.Entities.OfType<FuncDoor>().Single(door => door.Node.Parent?.Name == gate);
}
