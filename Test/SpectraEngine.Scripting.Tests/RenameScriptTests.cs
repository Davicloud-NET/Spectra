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
    private const string RenamesTwoNodes = """
        local door = script.Parent
        door.Name = "Gate"
        print(door.Name, door)
        door.Name = "Portcullis"
        script.Name = "Opener"
        print(door.Name, script)
        """;

    [Fact]
    public void A_name_a_script_gives_a_node_is_its_name_on_the_next_line()
    {
        using var level = new ScriptLevel();
        SceneNode door = level.Part("Door", new Vector3(1f, 2f, 3f));
        level.AddScript(door, RenamesTwoNodes);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Gate Gate", "Portcullis Opener"]);
        door.Name.ShouldBe("Portcullis");
    }

    [Fact]
    public void Stop_gives_back_the_names_a_script_changed()
    {
        using var level = new ScriptLevel();
        SceneNode door = level.Part("Door", new Vector3(1f, 2f, 3f));
        level.AddScript(door, RenamesTwoNodes);
        level.Play().Tick();

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
            print(pcall(function() door.Name = "two\u{2028}lines" end))
            print(pcall(function() door.Name = "Door " end))
            print(pcall(function() door.Name = "!self" end))
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
            $"false {here}:9: Door cannot be renamed: a name cannot hold a line break or any other control character.",
            $"false {here}:10: Door cannot be renamed: a name cannot start or end with a space.",
            $"false {here}:11: Door cannot be renamed: a name cannot start with !, since no wire can reach one that does.",
            "100",
        ]);
        level.Sink.Messages.Last().Line.ShouldBe(14);
        level.Errors.ShouldHaveSingleItem().ShouldEndWith("cannot be renamed: a name cannot be empty.");
        door.Name.ShouldBe(new string('x', 100));
    }

    // A map can hold a name that no script could give a node.
    [Fact]
    public void A_script_can_write_back_the_name_it_read_whatever_the_name()
    {
        using var level = new ScriptLevel();
        SceneNode door = level.Part("two\tparts", Vector3.Zero);
        level.AddScript(door, """
            local door = script.Parent
            door.Name = door.Name
            print("written")
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["written"]);
        door.Name.ShouldBe("two\tparts");
    }
}
