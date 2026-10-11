using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A script that sets a node's Name: the rename is live at once, a name a
/// node cannot have is an error, and Stop gives the old name back.
/// </summary>
public sealed class RenameScriptTests
{
    [Fact]
    public void A_script_renames_a_node_and_Stop_gives_the_name_back()
    {
        using var level = new ScriptLevel();
        SceneNode door = level.Part("Door", new Vector3(1f, 2f, 3f));
        level.AddScript(door, """
            local door = script.Parent
            door.Name = "Gate"
            print(door.Name, door)
            door.Name = "Portcullis"
            script.Name = "Opener"
            print(door.Name, script)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Gate Gate", "Portcullis Opener"]);
        door.Name.ShouldBe("Portcullis");

        level.Stop();

        door.Name.ShouldBe("Door");
        door.Children.ShouldHaveSingleItem().Name.ShouldBe("Script");
    }

    [Fact]
    public void A_wire_finds_an_entity_by_the_name_a_script_gave_it()
    {
        using var level = new ScriptLevel();
        SceneNode relay = level.Scene.Root.CreateChild("Relay");
        relay.Entity = new EntityData(SayingEntity.Class);
        relay.Entity.Connections.Add(new EntityConnection(
            SayingEntity.Said, "Speaker", "Say", "heard", 0f, EntityConnection.Infinite));

        SceneNode gate = level.Part("Gate", Vector3.Zero);
        gate.Entity = new EntityData(SayingEntity.Class);
        level.AddScript(gate, """
            task.wait(0.05)
            script.Parent.Name = "Speaker"
            """);

        // Before the rename nothing answers to the wire's target.
        level.Play();
        level.Entities.QueueInput("Relay", "Say", "first");
        level.Tick(2);
        level.Printed.ShouldBe(["first"]);

        level.Tick(5);
        gate.Name.ShouldBe("Speaker");
        level.Entities.QueueInput("Relay", "Say", "second");
        level.Entities.QueueInput("Gate", "Say", "nobody");
        level.Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["first", "second", "heard"]);

        level.Stop();
        gate.Name.ShouldBe("Gate");
    }

    [Fact]
    public void A_name_a_node_cannot_have_is_an_error_at_the_scripts_line()
    {
        using var level = new ScriptLevel();
        SceneNode door = level.Part("Door", Vector3.Zero);
        level.AddScript(door, """
            local door = script.Parent
            print(pcall(function() door.Name = "" end))
            print(pcall(function() door.Name = "   " end))
            print(pcall(function() door.Name = "two\nlines" end))
            print(pcall(function() door.Name = string.rep("x", 101) end))
            print(pcall(function() door.Name = "\xff\xfe" end))
            print(pcall(function() door.Name = 5 end))
            print(pcall(function() door.Name = nil end))
            door.Name = string.rep("x", 100)
            print(#door.Name)
            door.Name = ""
            print("not reached")
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"false {here}:2: Door cannot be renamed: a name cannot be empty.",
            $"false {here}:3: Door cannot be renamed: a name cannot be empty.",
            $"false {here}:4: Door cannot be renamed: a name cannot hold a line break or any other control character.",
            $"false {here}:5: Door cannot be renamed: a name is at most 100 characters long, and this one has 101.",
            $"false {here}:6: Unable to assign property Name. The string is not valid UTF-8",
            $"false {here}:7: Unable to assign property Name. string expected, got number",
            $"false {here}:8: Unable to assign property Name. string expected, got nil",
            "100",
        ]);
        level.Sink.Messages.Last().Line.ShouldBe(11);
        level.Errors.ShouldHaveSingleItem().ShouldEndWith("cannot be renamed: a name cannot be empty.");
        door.Name.ShouldBe(new string('x', 100));
    }
}
