using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Entities;
using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Two things a level makes while it plays, through a play session with no
/// window: coins a script clones from a template and destroys when the
/// player walks into them, and a gate that is cloned with its door, its
/// button and the script that joins the two.
/// </summary>
public sealed class SpawnGameTests
{
    private const int Patience = 900;

    // A coin every half second, each two units further along the player's way.
    private const string Spawner = """
        local template = workspace.Coin
        local spawned, collected = 0, 0
        while true do
            task.wait(0.5)
            spawned += 1
            local coin = template:Clone()
            coin.Name = "Coin" .. spawned
            coin.Position = Vector3.new(2 + 2 * spawned, 1, 0)
            coin.Touched:Connect(function(character)
                collected += 1
                print("collected", coin.Name, collected, character == game:GetService("Players").LocalPlayer.Character)
                coin:Destroy()
            end)
            coin.Parent = workspace
        end
        """;

    private const string Opener = """
        local door = script.Parent
        local button = door.Parent.Button
        button.OnPressed:Connect(function()
            print(door.Parent.Name .. " opens")
            door:Open()
        end)
        """;

    private const string Builder = """
        local gate = workspace.Gate
        local copy = gate:Clone()
        copy.Name = "Gate2"
        copy.Position += Vector3.new(0, 0, 10)
        copy.Parent = workspace
        task.wait(0.1)
        copy.Button:Press()
        task.wait(1)
        gate.Button:Press()
        """;

    [Fact]
    public void A_coin_is_cloned_every_half_second_and_destroyed_when_the_player_walks_into_it()
    {
        using var level = new ScriptLevel();
        TouchParts.Box(level, "Coin", new Vector3(0f, 1f, 40f), new Vector3(0.25f, 1f, 2f), isSolid: false);
        level.AddScript(level.Scene.Root, Spawner, "Spawner");
        var before = new LevelState(level.Scene);
        int built = level.Scene.NodeCount;
        int compiles = level.Scene.StaticWorldCompileCount;

        level.Play();
        for (int tick = 0; tick < Patience && level.Printed.Length < 5; tick++)
            Tick(level, Forward());

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "collected Coin1 1 true", "collected Coin2 2 true", "collected Coin3 3 true", "collected Coin4 4 true",
            "collected Coin5 5 true",
        ]);

        // What is left are the coins the player has not come to yet.
        TreeWrites tree = new ScriptWrites(level.Entities).Tree;
        string[] coins = [.. level.Scene.Root.Children.Select(node => node.Name).Where(name => name.StartsWith("Coin", StringComparison.Ordinal))];
        coins.ShouldNotContain("Coin5");
        coins.ShouldContain("Coin");
        tree.MadeCount.ShouldBe(coins.Length - 1);
        level.Scene.NodeCount.ShouldBe(built + tree.MadeCount);
        level.Entities.Listeners.Count.ShouldBe(tree.MadeCount);
        level.Runtime.ShouldNotBeNull().NodeSignalCount.ShouldBe(tree.MadeCount);

        // None of it cost a compile of the level's world.
        level.Scene.StaticWorldDirty.ShouldBeFalse();
        level.Scene.StaticWorldCompileCount.ShouldBe(compiles);

        level.Stop();

        level.Scene.NodeCount.ShouldBe(built);
        new LevelState(level.Scene).ShouldBe(before, "after the coins");
    }

    [Fact]
    public void A_gate_cloned_with_its_script_opens_on_its_own_button_and_the_first_gate_on_its_own()
    {
        using var level = new ScriptLevel();
        SceneNode gate = Gate(level);
        level.AddScript(level.Scene.Root, Builder, "Builder");
        var before = new LevelState(level.Scene);

        level.Play().Tick(30);

        FuncDoor first = Door(level, gate);
        SceneNode copy = level.Scene.Root.Children.Single(node => node.Name == "Gate2");
        FuncDoor second = Door(level, copy);
        level.Printed.ShouldBe(["Gate2 opens"]);
        second.IsFullyOpen.ShouldBeTrue();
        first.TicksTravelled.ShouldBe(0);
        Vector3.Distance(copy.WorldPosition, gate.WorldPosition + new Vector3(0f, 0f, 10f)).ShouldBeLessThan(1e-4f);

        level.Tick(60);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Gate2 opens", "Gate opens"]);
        first.IsFullyOpen.ShouldBeTrue();
        level.Entities.Entities.OfType<FuncButton>().Count().ShouldBe(2);
        level.Runtime.ShouldNotBeNull().RunningScriptCount.ShouldBe(3);

        level.Stop();

        new LevelState(level.Scene).ShouldBe(before, "after the gates");
    }

    // A door and a button with no wire between them, and under the door the
    // script that opens it when the button is pressed. Away from where the
    // player starts, so a door squeezes nobody.
    private static SceneNode Gate(ScriptLevel level)
    {
        SceneNode gate = level.Scene.Root.CreateChild("Gate");
        gate.LocalPosition = new Vector3(20f, 0f, 20f);

        var button = new EntityData("func_button");
        button.SetValue("distance", "0.1");
        button.SetValue("speed", "6");
        button.SetValue("wait", "0");
        level.Part("Button", new Vector3(0f, 1f, 0f), gate).Entity = button;

        var door = new EntityData("func_door");
        door.SetValue("distance", "1");
        door.SetValue("speed", "6");
        door.SetValue("wait", "-1");
        SceneNode doorNode = level.Part("Door", new Vector3(4f, 1f, 0f), gate);
        doorNode.Entity = door;
        level.AddScript(doorNode, Opener, "Opener");
        return gate;
    }

    private static FuncDoor Door(ScriptLevel level, SceneNode gate) =>
        level.Entities.Entities.OfType<FuncDoor>().Single(door => ReferenceEquals(door.Node.Parent, gate));
}
