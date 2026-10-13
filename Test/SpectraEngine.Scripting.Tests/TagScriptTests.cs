using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Tags on a node, through the node's own methods and through
/// CollectionService: each means what it means in Roblox.
/// </summary>
public sealed class TagScriptTests
{
    private const string Service = "local CollectionService = game:GetService(\"CollectionService\")\n";

    [Fact]
    public void A_tag_put_on_a_node_is_one_it_has_until_it_is_taken_off()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            print(door:HasTag("Open"))
            door:AddTag("Open")
            print(door:HasTag("Open"), door:HasTag("open"), workspace.House:HasTag("Open"), door.Hinge:HasTag("Open"))
            door:RemoveTag("Open")
            print(door:HasTag("Open"), select("#", door:AddTag("Open")), select("#", door:RemoveTag("Open")))
            """);

        printed.ShouldBe(["false", "true false false false", "false 0 0"]);
    }

    [Fact]
    public void A_node_has_its_tags_in_the_order_it_got_them()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            print(#door:GetTags(), #workspace:GetTags())
            door:AddTag("Wooden")
            door:AddTag("Open")
            door:AddTag("A tag with spaces, and more")
            door:AddTag("Open")
            print(table.concat(door:GetTags(), " | "))
            door:RemoveTag("Wooden")
            door:AddTag("Wooden")
            print(table.concat(door:GetTags(), " | "))
            """);

        printed.ShouldBe(
        [
            "0 0",
            "Wooden | Open | A tag with spaces, and more",
            "Open | A tag with spaces, and more | Wooden",
        ]);
    }

    [Fact]
    public void Every_kind_of_node_takes_a_tag_and_the_root_does_too()
    {
        string[] printed = House.Ask(Service + """
            local nodes = { workspace, workspace.House, workspace.House.Door, workspace.House.Lamp,
                workspace.House.Sign, workspace.Relay, workspace.Rules, script, workspace.Baseplate }
            for _, node in nodes do
                node:AddTag("Marked")
            end
            local marked = {}
            for _, node in CollectionService:GetTagged("Marked") do
                table.insert(marked, node.Name)
            end
            print(table.concat(marked, " "))
            """);

        printed.ShouldBe(["Root Baseplate House Door Lamp Sign Relay Rules Main"]);
    }

    [Fact]
    public void The_service_does_what_a_nodes_own_methods_do()
    {
        string[] printed = House.Ask(Service + """
            local door = workspace.House.Door
            print(CollectionService, typeof(CollectionService), CollectionService == game:GetService("CollectionService"))
            print(CollectionService:HasTag(door, "Open"))
            CollectionService:AddTag(door, "Open")
            print(CollectionService:HasTag(door, "Open"), door:HasTag("Open"))
            door:AddTag("Wooden")
            print(table.concat(CollectionService:GetTags(door), " "), #CollectionService:GetTags(workspace))
            CollectionService:RemoveTag(door, "Open")
            print(door:HasTag("Open"), CollectionService:HasTag(door, "Wooden"))
            print(CollectionService.HasTag(CollectionService, door, "Wooden"))
            """);

        printed.ShouldBe(
        [
            "CollectionService Instance true",
            "false",
            "true true",
            "Open Wooden 0",
            "false true",
            "true",
        ]);
    }

    [Fact]
    public void GetTagged_gives_the_nodes_of_one_tag_and_an_empty_table_for_a_tag_no_node_has()
    {
        string[] printed = House.Ask(Service + """
            local house = workspace.House
            house.Door:AddTag("Opens")
            house.Lamp:AddTag("Lit")
            house.Lamp.Shade:AddTag("Lit")
            local lit = CollectionService:GetTagged("Lit")
            print(#lit, lit[1] == house.Lamp, lit[2] == house.Lamp.Shade, typeof(lit[1]))
            print(#CollectionService:GetTagged("Opens"), #CollectionService:GetTagged("Nobody"), #CollectionService:GetTagged(""))
            print(CollectionService:GetTagged("Lit") == lit)
            house.Lamp:RemoveTag("Lit")
            print(#lit, #CollectionService:GetTagged("Lit"), CollectionService:GetTagged("Lit")[1])
            """);

        // A table of its own each time: what a script does to one changes no other.
        printed.ShouldBe(["2 true true Instance", "1 0 0", "false", "2 1 Shade"]);
    }

    [Fact]
    public void Two_scripts_reach_one_tag()
    {
        using var level = new ScriptLevel();
        level.Part("Crate", new Vector3(3f, 1f, 3f));
        level.AddScript(level.Scene.Root, "workspace.Crate:AddTag('Heavy')", "Tagger");
        level.AddScript(level.Scene.Root, """
            local crate = workspace.Crate
            print(crate:HasTag("Heavy"), #game:GetService("CollectionService"):GetTagged("Heavy"))
            crate:RemoveTag("Heavy")
            """, "Reader");
        level.AddScript(level.Scene.Root, "print(workspace.Crate:HasTag('Heavy'))", "Last");

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true 1", "false"]);
    }

    [Theory]
    [InlineData("door:AddTag(5)", "invalid argument #1 to 'AddTag' (string expected, got number)")]
    [InlineData("door:AddTag()", "invalid argument #1 to 'AddTag' (string expected, got nil)")]
    [InlineData("door:RemoveTag(door)", "invalid argument #1 to 'RemoveTag' (string expected, got Instance)")]
    [InlineData("door:HasTag(true)", "invalid argument #1 to 'HasTag' (string expected, got boolean)")]
    [InlineData("door:AddTag('\\255')", "AddTag was handed a tag that is not valid UTF-8, which no node can have")]
    [InlineData("tags:AddTag('Open', door)", "invalid argument #1 to 'AddTag' (Instance expected, got string)")]
    [InlineData("tags:AddTag(door)", "invalid argument #2 to 'AddTag' (string expected, got nil)")]
    [InlineData("tags:RemoveTag(game, 'Open')", "invalid argument #1 to 'RemoveTag' (Instance expected, got DataModel)")]
    [InlineData("tags:HasTag(nil, 'Open')", "invalid argument #1 to 'HasTag' (Instance expected, got nil)")]
    [InlineData("tags:HasTag(door, {})", "invalid argument #2 to 'HasTag' (string expected, got table)")]
    [InlineData("tags:GetTags('Open')", "invalid argument #1 to 'GetTags' (Instance expected, got string)")]
    [InlineData("tags:GetTagged(door)", "invalid argument #1 to 'GetTagged' (string expected, got Instance)")]
    [InlineData("tags:GetInstanceAddedSignal()", "invalid argument #1 to 'GetInstanceAddedSignal' (string expected, got nil)")]
    [InlineData("tags:GetInstanceRemovedSignal(7)", "invalid argument #1 to 'GetInstanceRemovedSignal' (string expected, got number)")]
    [InlineData(
        "tags:GetInstanceAddedSignal('\\255')",
        "GetInstanceAddedSignal was handed a tag that is not valid UTF-8, which no node can have")]
    [InlineData("door.AddTag('Open')", "Expected ':' not '.' calling member function AddTag")]
    [InlineData("tags.GetTagged('Open')", "Expected ':' not '.' calling member function GetTagged")]
    [InlineData("tags.Lava = true", "Lava is not a valid member of CollectionService \"CollectionService\"")]
    [InlineData("tags.GetTagged = nil", "Unable to assign property GetTagged. Property is read only")]
    public void A_tag_that_is_no_tag_and_a_node_that_is_no_node_are_refused_with_these_words(string call, string words)
    {
        using ScriptLevel level = House.With($"""
            local tags = game:GetService("CollectionService")
            local door = workspace.House.Door
            {call}
            """);

        level.Play().Tick();

        level.Errors.ShouldHaveSingleItem().ShouldBe(words);
    }

    // Bytes that are no text name no tag, so no node has one and none is listed.
    [Fact]
    public void A_tag_that_is_no_text_is_one_no_node_has()
    {
        string[] printed = House.Ask(Service + """
            local door = workspace.House.Door
            door:RemoveTag("\255")
            print(door:HasTag("\255"), #CollectionService:GetTagged("\255"), #door:GetTags())
            """);

        printed.ShouldBe(["false 0 0"]);
    }
}
