using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Touched and TouchEnded of a part, heard by a script while a real mover
/// walks the level: standing on a part, pushing against one and walking
/// through one. Each fires once for a touch, with the player's character.
/// </summary>
public sealed class TouchScriptTests
{
    private const string Refusal =
        "Baseplate cannot tell what touches it: it is a world brush, fused into the level, and only parts " +
        "notice a touch while a level plays. Convert the brush to a part.";

    [Fact]
    public void A_part_the_player_lands_on_is_touched_once_and_left_once_when_the_player_walks_off()
    {
        using var level = new ScriptLevel();
        SceneNode pad = TouchParts.Pad(level, "Pad", x: 0f);
        level.AddScript(pad, TouchParts.Reporter);
        level.Play();

        level.Tick(120);
        level.Printed.ShouldBe(["Pad touched true"]);
        Walker(level).State.GroundNodeId.ShouldBe(pad.Id);

        Tick(level, Forward(), 60);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Pad touched true", "Pad left true"]);
    }

    [Fact]
    public void A_wall_the_player_pushes_against_is_touched_once_for_as_long_as_the_player_pushes()
    {
        using var level = new ScriptLevel();
        level.AddScript(TouchParts.Wall(level, "Wall", x: 3f, isSolid: true), TouchParts.Reporter);
        level.Play();

        Tick(level, Forward(), 180);
        level.Printed.ShouldBe(["Wall touched true"]);
        Walker(level).State.Position.X.ShouldBe(2.5f - Walker(level).Tuning.Radius, 0.03f);

        // Still against it and no longer pushing is still a touch.
        level.Tick(60);
        level.Printed.Length.ShouldBe(1);

        Tick(level, Forward(MathF.PI), 30);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Wall touched true", "Wall left true"]);
    }

    [Fact]
    public void A_part_that_is_not_solid_is_touched_while_the_player_walks_through_it()
    {
        using var level = new ScriptLevel();
        level.AddScript(TouchParts.Wall(level, "Zone", x: 3f, isSolid: false), TouchParts.Reporter);
        level.Play();
        level.Tick(60);
        level.Printed.ShouldBeEmpty();

        Tick(level, Forward(), 90);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Zone touched true", "Zone left true"]);
        Walker(level).State.Position.X.ShouldBeGreaterThan(4f);
    }

    [Fact]
    public void Two_parts_the_player_stands_on_are_touched_in_one_tick_in_the_order_a_script_listened_to_them()
    {
        using var level = new ScriptLevel();
        TouchParts.Pad(level, "Left", x: -1f);
        TouchParts.Pad(level, "Right", x: 1f);
        level.AddScript(level.Scene.Root, """
            local beats = 0
            game:GetService("RunService").Heartbeat:Connect(function() beats += 1 end)
            workspace.Right.Touched:Connect(function() print("right", beats) end)
            workspace.Left.Touched:Connect(function() print("left", beats) end)
            """);

        level.Play().Tick(120);

        level.Errors.ShouldBeEmpty();
        level.Printed.Length.ShouldBe(2);
        level.Printed[0].ShouldStartWith("right ");
        level.Printed[1].ShouldBe(level.Printed[0].Replace("right", "left"));
        level.Entities.Touches.PartTouchCount.ShouldBe(2);
    }

    [Fact]
    public void A_listener_that_disconnects_in_its_own_call_is_not_called_for_the_next_touch()
    {
        using var level = new ScriptLevel();
        level.AddScript(TouchParts.Pad(level, "Pad", x: 0f), """
            local pad = script.Parent
            local connection
            connection = pad.Touched:Connect(function()
                print("first", connection.Connected)
                connection:Disconnect()
            end)
            pad.Touched:Connect(function() print("second") end)
            """);
        level.Play().Tick(120);
        level.Printed.ShouldBe(["first true", "second"]);

        // Away and back onto it.
        Walker(level).Teleport(new Vector3(10f, 0.05f, 0f));
        level.Tick(30);
        Walker(level).Teleport(new Vector3(0f, 0.2f, 0f));
        level.Tick(60);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["first true", "second", "second"]);
    }

    // Until a script can destroy a node, a test takes it out of the scene.
    [Fact]
    public void A_part_taken_out_of_the_scene_while_touched_ends_its_touch_and_nothing_fails()
    {
        using var level = new ScriptLevel();
        SceneNode pad = TouchParts.Pad(level, "Pad", x: 0f);
        level.AddScript(pad, TouchParts.Reporter);
        level.Play().Tick(120);

        level.Scene.Root.RemoveChild(pad);
        level.Tick(60);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Pad touched true", "Pad left true"]);
        level.Entities.Touches.PartTouchCount.ShouldBe(0);
        Walker(level).State.Grounded.ShouldBeTrue();
    }

    [Fact]
    public void A_script_that_switches_CanTouch_off_ends_the_touch_and_on_starts_another()
    {
        using var level = new ScriptLevel();
        level.AddScript(TouchParts.Pad(level, "Pad", x: 0f), $"""
            {TouchParts.Reporter}
            task.wait(2)
            part.CanTouch = false
            task.wait(0.5)
            print("off", part.CanTouch)
            part.CanTouch = true
            """);

        level.Play().Tick(240);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Pad touched true", "Pad left true", "off false", "Pad touched true"]);
    }

    [Fact]
    public void Stop_tells_no_listener_that_its_touch_ended_and_the_next_play_starts_clean()
    {
        using var level = new ScriptLevel();
        level.AddScript(TouchParts.Pad(level, "Pad", x: 0f), TouchParts.Reporter);
        level.Play().Tick(120);
        EntityWorld played = level.Entities;
        played.Touches.PartTouchCount.ShouldBe(1);

        level.Stop();

        level.Printed.ShouldBe(["Pad touched true"]);
        played.Touches.PartTouchCount.ShouldBe(0);
        played.Listeners.Count.ShouldBe(0);

        level.Play().Tick(120);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Pad touched true", "Pad touched true"]);
        level.Entities.Touches.PartTouchCount.ShouldBe(1);
    }

    [Fact]
    public void A_level_with_nothing_walking_takes_a_listener_and_never_calls_it()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(TouchParts.Pad(level, "Pad", x: 0f), """
            print(game:GetService("Players").LocalPlayer)
            script.Parent.Touched:Connect(function() print("touched") end)
            script.Parent.TouchEnded:Connect(function() print("left") end)
            """);

        level.Play().Tick(120);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["nil"]);
        level.Entities.Listeners.TouchListenerCount.ShouldBe(2);
    }

    [Fact]
    public void Touched_is_one_signal_for_a_part_with_Once_and_Wait_like_any_other()
    {
        using var level = new ScriptLevel();
        level.AddScript(TouchParts.Pad(level, "Pad", x: 0f), $$"""
            {{Character}}
            local pad = script.Parent
            print(pad.Touched == pad.Touched, pad.Touched ~= pad.TouchEnded, tostring(pad.Touched), tostring(pad.TouchEnded))
            pad.Touched:Once(function(hit) print("once", hit == character) end)
            task.spawn(function()
                print("waited", pad.Touched:Wait() == character)
                print("waited again", pad.Touched:Wait() == character)
            end)
            """);
        level.Play().Tick(120);
        level.Printed.ShouldBe(["true true Signal Touched Signal TouchEnded", "once true", "waited true"]);

        // The thread waits again while the player still stands there, and
        // hears nothing until the next touch starts.
        level.Tick(60);
        level.Printed.Length.ShouldBe(3);

        Walker(level).Teleport(new Vector3(10f, 0.05f, 0f));
        level.Tick(30);
        Walker(level).Teleport(new Vector3(0f, 0.2f, 0f));
        level.Tick(60);

        level.Errors.ShouldBeEmpty();
        level.Printed[3..].ShouldBe(["waited again true"]);
    }

    [Fact]
    public void A_listener_is_called_from_the_queue_in_the_tick_the_touch_is_found()
    {
        using var level = new ScriptLevel();
        level.AddScript(TouchParts.Wall(level, "Zone", x: 0f, isSolid: false), """
            local beats = 0
            game:GetService("RunService").Heartbeat:Connect(function() beats += 1 end)
            script.Parent.Touched:Connect(function() print("touched after", beats) end)
            """);

        level.Play().Tick(3);

        // The player starts inside. The first tick starts the script, finds
        // the touch and calls the listener, all ahead of its own Heartbeat.
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["touched after 0"]);
    }

    [Theory]
    [InlineData("floor.Touched:Connect(function() end)")]
    [InlineData("floor.Touched:Once(function() end)")]
    [InlineData("floor.Touched:Wait()")]
    [InlineData("floor.TouchEnded:Connect(function() end)")]
    public void Listening_for_a_touch_of_a_world_brush_is_an_error_that_says_to_use_a_part(string listen)
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $"""
            local floor = workspace.Baseplate
            print(tostring(floor.Touched))
            print(select(2, pcall(function() {listen} end)))
            """);

        level.Play().Tick(60);

        level.Errors.ShouldBeEmpty();
        level.Printed.Length.ShouldBe(2);
        level.Printed[0].ShouldBe("Signal Touched");
        level.Printed[1].ShouldEndWith(Refusal);
        level.Entities.Listeners.Count.ShouldBe(0);
    }

    [Fact]
    public void A_mesh_is_touched_where_the_player_walks_into_the_box_around_it()
    {
        using var level = new ScriptLevel();
        SceneNode coin = SpectraEngine.Bsp.Tests.SpatialTestHelpers.CreateMeshNode(
            level.Scene.Root, "Coin", new Vector3(3f, 1f, 0f));
        level.AddScript(coin, TouchParts.Reporter);
        level.Play();

        Tick(level, Forward(), 90);

        // A mesh stops nobody, so the player walks on through.
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Coin touched true", "Coin left true"]);
    }

    [Fact]
    public void A_part_with_a_listener_only_for_the_end_of_a_touch_hears_it()
    {
        using var level = new ScriptLevel();
        level.AddScript(TouchParts.Pad(level, "Pad", x: 0f), """
            script.Parent.TouchEnded:Connect(function() print("left") end)
            """);
        level.Play().Tick(120);
        level.Printed.ShouldBeEmpty();

        Tick(level, Forward(), 60);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["left"]);
    }
}
