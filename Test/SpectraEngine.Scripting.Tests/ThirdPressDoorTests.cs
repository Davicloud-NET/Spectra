using System.Numerics;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Entities;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A button and a door with no wire between them, and a script that opens
/// the door on the third press. Played through a play session with no
/// window, with the built-in classes, and watched by <c>ent_watch</c>.
/// </summary>
public sealed class ThirdPressDoorTests
{
    // A press every 12 ticks. The button is back out two ticks after one.
    private const string Presser = """
        local button = script.Parent
        for _ = 1, 3 do
            button:Press()
            task.wait(0.2)
        end
        """;

    private const string Opener = """
        local door = script.Parent
        local button = workspace.Button
        local presses = 0
        button.OnPressed:Connect(function()
            presses += 1
            print("press " .. presses)
            if presses == 3 then
                door:Open()
            end
        end)
        button.OnPressed:Connect(function() print("second listener") end)
        """;

    private const int ThirdPress = 25;
    private const int DoorTravel = 10;

    [Fact]
    public void The_door_stays_shut_for_two_presses_and_opens_tick_by_tick_from_the_third()
    {
        using var level = Level(out _, out SceneNode door);

        level.Play();
        FuncDoor live = Live<FuncDoor>(level, door);
        var travelled = new List<int>();
        for (int tick = 1; tick <= ThirdPress + DoorTravel + 1; tick++)
        {
            level.Tick();
            travelled.Add(live.TicksTravelled);
        }

        // Opened in the tick of the press: the listener ran from that tick's
        // queue, and the input it sent went out in the same drain.
        level.Errors.ShouldBeEmpty();
        live.TravelTicks.ShouldBe(DoorTravel);
        travelled.Take(ThirdPress - 1).ShouldAllBe(ticks => ticks == 0);
        travelled.Skip(ThirdPress - 1).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 10]);
        live.IsFullyOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_press_reaches_the_wires_first_in_their_order_and_then_the_listeners_in_theirs()
    {
        using var level = Level(out _, out _);

        level.Play().Tick(ThirdPress);

        string[] onePress = ["wire one", "wire two", "press {0}", "second listener"];
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            .. onePress.Select(line => string.Format(line, 1)),
            .. onePress.Select(line => string.Format(line, 2)),
            .. onePress.Select(line => string.Format(line, 3)),
        ]);
    }

    [Fact]
    public void The_watch_shows_what_the_scripts_sent_and_heard_beside_the_wires()
    {
        using var level = Level(out SceneNode button, out SceneNode door);
        var output = new ConsoleOutput();
        var watch = new EntityWatch(output);
        watch.TurnOn();
        level.Manager.EntityTrace = watch.ActiveTrace;

        level.Play().Tick(ThirdPress + DoorTravel);

        string presser = button.Children.Single().Script.ShouldNotBeNull().Path;
        string opener = door.Children.Single().Script.ShouldNotBeNull().Path;
        string[] lines = [.. output.Drain().Select(line => line.Text)];
        lines.Where(line => line.StartsWith($"ent {ThirdPress} ", StringComparison.Ordinal)).ShouldBe(
        [
            $"ent 25 send {presser}:3 -> Button.Press",
            "ent 25 fire Button.OnPressed wires=2 listeners=2",
            "ent 25 send Button.OnPressed -> Sayer.Say(\"wire one\")",
            "ent 25 fire Sayer.OnSaid wires=0",
            "ent 25 send Button.OnPressed -> Sayer.Say(\"wire two\")",
            "ent 25 fire Sayer.OnSaid wires=0",
            $"ent 25 send {opener}:8 -> Door.Open",
            "ent 25 fire Door.OnOpen wires=0",
            "ent 25 fire Button.OnIn wires=0",
        ]);

        // The first two presses sent the door nothing.
        lines.Count(line => line.Contains("-> Door.", StringComparison.Ordinal)).ShouldBe(1);
        lines.Count(line => line.Contains("-> Button.Press", StringComparison.Ordinal)).ShouldBe(3);
        lines.ShouldContain($"ent {ThirdPress + DoorTravel - 1} fire Door.OnFullyOpen wires=0");
    }

    [Fact]
    public void Stop_puts_the_door_back_and_lets_the_listeners_go()
    {
        using var level = Level(out _, out SceneNode door);
        Vector3 authored = door.LocalPosition;
        level.Play().Tick(ThirdPress + DoorTravel);
        EntityWorld world = level.Entities;
        door.LocalPosition.ShouldNotBe(authored);
        world.Listeners.Count.ShouldBe(2);

        level.Stop();

        door.LocalPosition.ShouldBe(authored);
        world.Listeners.Count.ShouldBe(0);
    }

    // Away from where the player starts, so the door squeezes nobody. The
    // button's two wires go to an entity that prints, and none to the door.
    private static ScriptLevel Level(out SceneNode button, out SceneNode door)
    {
        var level = new ScriptLevel();

        level.Scene.Root.CreateChild("Sayer").Entity = new EntityData(SayingEntity.Class);

        button = level.Part("Button", new Vector3(20f, 1f, 20f));
        var buttonData = new EntityData("func_button");
        buttonData.SetValue("distance", "0.1");
        buttonData.SetValue("speed", "6");
        buttonData.SetValue("wait", "0");
        buttonData.Connections.Add(new EntityConnection(FuncButton.OnPressed, "Sayer", "Say", "wire one", 0f, EntityConnection.Infinite));
        buttonData.Connections.Add(new EntityConnection(FuncButton.OnPressed, "Sayer", "Say", "wire two", 0f, EntityConnection.Infinite));
        button.Entity = buttonData;

        door = level.Part("Door", new Vector3(24f, 1f, 20f));
        var doorData = new EntityData("func_door");
        doorData.SetValue("distance", "1");
        doorData.SetValue("speed", "6");
        doorData.SetValue("wait", "-1");
        door.Entity = doorData;

        level.AddScript(button, Presser, "Presser");
        level.AddScript(door, Opener, "Opener");
        return level;
    }

    private static T Live<T>(ScriptLevel level, SceneNode node)
        where T : Entity =>
        level.Entities.Entities.OfType<T>().Single(entity => ReferenceEquals(entity.Node, node));
}
