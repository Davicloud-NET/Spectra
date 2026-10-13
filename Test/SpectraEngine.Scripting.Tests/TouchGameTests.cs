using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a Roblox developer builds first, played through a play session with
/// a player that walks by commands and no window: a kill brick, a
/// checkpoint, and parts that move into a player who stands still. And a
/// trigger entity whose part a script listens to as well.
/// </summary>
public sealed class TouchGameTests
{
    private const int Patience = 600;

    [Fact]
    public void A_kill_brick_sends_the_player_who_steps_on_it_back_to_the_start_every_time()
    {
        using var level = new ScriptLevel();
        level.AddScript(TouchParts.Pad(level, "KillBrick", x: 4f), """
            local kills = 0
            script.Parent.Touched:Connect(function(character)
                kills += 1
                print("killed", kills)
                character:Respawn()
            end)
            """);
        level.Play();
        CharacterSimulation walker = Walker(level);

        WalkUntilPrinted(level, 1);

        // Back at the start in the tick of the touch, and on its way again.
        level.Printed.ShouldBe(["killed 1"]);
        Vector3.Distance(walker.State.Position, walker.SpawnPosition).ShouldBeLessThan(0.1f);
        walker.TryTakeTeleport(out _).ShouldBeTrue();

        WalkUntilPrinted(level, 2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["killed 1", "killed 2"]);
    }

    [Fact]
    public void A_checkpoint_remembers_where_the_player_passed_and_a_brick_sends_the_player_back_there()
    {
        using var level = new ScriptLevel();
        TouchParts.Wall(level, "Checkpoint", x: 3f, isSolid: false);
        TouchParts.Wall(level, "Brick", x: 7f, isSolid: true);
        level.AddScript(level.Scene.Root, """
            local checkpoint = workspace.Checkpoint
            local saved

            checkpoint.Touched:Connect(function()
                saved = Vector3.new(checkpoint.Position.X, 0.05, checkpoint.Position.Z)
                print("saved")
            end)

            workspace.Brick.Touched:Connect(function(character)
                if saved then
                    print("sent back")
                    character:Teleport(saved)
                else
                    print("no checkpoint yet")
                    character:Respawn()
                end
            end)
            """);
        level.Play();
        CharacterSimulation walker = Walker(level);

        WalkUntilPrinted(level, 2);

        level.Printed.ShouldBe(["saved", "sent back"]);
        walker.State.Position.X.ShouldBeInRange(3f, 3.1f);
        walker.State.Position.Z.ShouldBe(0f, 1e-3f);

        // Put inside the checkpoint, the player touches it again and walks
        // on to the brick again.
        WalkUntilPrinted(level, 4);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["saved", "sent back", "saved", "sent back"]);
    }

    [Fact]
    public void A_part_on_a_lift_that_rises_into_the_standing_player_is_touched_once_and_carries_the_player()
    {
        using var level = new ScriptLevel();
        SceneNode lift = TouchParts.Box(level, "Lift", new Vector3(0f, -1f, 0f), new Vector3(1f, 0.5f, 1f));
        SceneNode plate = TouchParts.Box(
            level, "Plate", new Vector3(0f, 0.55f, 0f), new Vector3(0.5f, 0.05f, 0.5f), parent: lift);
        level.AddScript(plate, TouchParts.Reporter);
        level.AddScript(lift, """
            local lift = script.Parent
            local risen = 0
            task.wait(1)
            game:GetService("RunService").Heartbeat:Connect(function()
                if risen < 100 then
                    risen += 1
                    lift.Position += Vector3.new(0, 0.01, 0)
                end
            end)
            """);
        level.Play();
        CharacterSimulation walker = Walker(level);

        // Under the floor until the lift has risen for a while.
        level.Tick(80);
        level.Printed.ShouldBeEmpty();
        walker.State.Position.Y.ShouldBeLessThan(0.05f);

        level.Tick(160);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Plate touched true"]);
        walker.State.GroundNodeId.ShouldBe(plate.Id);
        walker.State.Position.Y.ShouldBe(0.6f, 0.03f);
    }

    [Fact]
    public void A_door_that_slides_into_the_standing_player_is_touched_once_while_it_pushes_and_left_as_it_goes_back()
    {
        using var level = new ScriptLevel();
        SceneNode door = TouchParts.Box(level, "Door", new Vector3(2f, 1f, 0f), new Vector3(0.25f, 1f, 1f));
        level.AddScript(door, $"""
            {TouchParts.Reporter}
            local moved = 0
            task.wait(1)
            game:GetService("RunService").Heartbeat:Connect(function()
                moved += 1
                if moved <= 100 then
                    part.Position -= Vector3.new(0.02, 0, 0)
                elseif moved <= 200 then
                    part.Position += Vector3.new(0.02, 0, 0)
                end
            end)
            """);
        level.Play();
        CharacterSimulation walker = Walker(level);

        level.Tick(60 + 100);
        level.Printed.ShouldBe(["Door touched true"]);
        walker.State.Position.X.ShouldBeLessThan(-0.5f);

        level.Tick(100);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Door touched true", "Door left true"]);
    }

    // The trigger's callback fires its outputs as the pass finds the touch.
    // Each queues its wires and then its listeners, and what a script hears
    // of the part's own touch is queued after them all.
    [Fact]
    public void A_triggers_own_outputs_reach_their_wires_and_listeners_ahead_of_the_Touched_of_its_part()
    {
        using var level = new ScriptLevel();
        SceneNode zone = TouchParts.Box(level, "Zone", new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 1f), isSolid: false);
        zone.CanQuery = false;
        zone.Entity = new EntityData("trigger_multiple");
        zone.Entity.Keyvalues.Add(new("wait", "0"));
        zone.Entity.Connections.Add(new EntityConnection("OnStartTouch", "Echo", "Ping", "", 0f, EntityConnection.Infinite));
        zone.Entity.Connections.Add(new EntityConnection("OnEndTouch", "Echo", "SayText", "gone", 0f, EntityConnection.Infinite));
        level.Scene.Root.CreateChild("Echo").Entity = new EntityData(EchoEntity.Class);
        level.AddScript(level.Scene.Root, """
            local zone = workspace.Zone
            zone.Touched:Connect(function() print("script: Touched") end)
            zone.TouchEnded:Connect(function() print("script: TouchEnded") end)
            zone.OnStartTouch:Connect(function() print("script: OnStartTouch") end)
            zone.OnTrigger:Connect(function() print("script: OnTrigger") end)
            zone.OnEndTouch:Connect(function() print("script: OnEndTouch") end)
            """);

        level.Play().Tick(2);
        level.Printed.ShouldBe(["Echo got Ping ''", "script: OnStartTouch", "script: OnTrigger", "script: Touched"]);

        Tick(level, Forward(), 60);

        level.Errors.ShouldBeEmpty();
        level.Printed[4..].ShouldBe(["Echo got SayText 'gone'", "script: OnEndTouch", "script: TouchEnded"]);
    }

    // The sample luau-lsp checks, in a level that has what it names.
    [Fact]
    public void The_sample_script_of_touch_plays_a_coin_a_checkpoint_and_a_kill_brick()
    {
        using var level = new ScriptLevel();
        SceneNode coin = SpectraEngine.Bsp.Tests.SpatialTestHelpers.CreateMeshNode(
            level.Scene.Root, "Coin", new Vector3(2f, 1f, 0f));
        TouchParts.Wall(level, "Checkpoint", x: 5f, isSolid: true);
        TouchParts.Pad(level, "Lava", x: 9f);
        level.AddScript(level.Scene.Root, ScriptSamples.At("Scripts/touch.luau").Text);
        level.Play();

        WalkUntilPrinted(level, 6);

        level.Errors.ShouldBeEmpty();
        level.Printed[0].ShouldBe("true true");
        level.Printed[3].ShouldBe("checkpoint at 5, 1, 0 walking at 4.5");
        level.Printed[4].ShouldStartWith("off the lava");
        level.Printed[5].ShouldBe(level.Printed[3], "sent back into the checkpoint, the player touches it again");

        // The coin was taken once, and what took it is put back at Stop.
        (coin.IsRendered, coin.CanTouch).ShouldBe((false, false));
        level.Stop();
        (coin.IsRendered, coin.CanTouch).ShouldBe((true, true));
    }

    private static void WalkUntilPrinted(ScriptLevel level, int lines)
    {
        for (int tick = 0; tick < Patience && level.Printed.Length < lines; tick++)
            Tick(level, Forward());

        level.Printed.Length.ShouldBeGreaterThanOrEqualTo(lines, "the player walked and the script did not print");
    }
}
