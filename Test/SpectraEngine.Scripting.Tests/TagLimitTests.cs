using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// How much scripts can leave on nodes as tags. It is kept outside the
/// memory a script's budget counts, so it has bounds of its own, and a tag
/// nobody has or waits for is not kept.
/// </summary>
public sealed class TagLimitTests
{
    private const string Service = "local CollectionService = game:GetService(\"CollectionService\")\n";

    [Fact]
    public void A_tag_of_the_longest_length_is_a_tag_like_another()
    {
        string[] printed = House.Ask(Service + """
            local long = string.rep("t", 256)
            local door = workspace.House.Door
            door:AddTag(long)
            print(door:HasTag(long), door:GetTags()[1] == long, CollectionService:GetTagged(long)[1])
            print(CollectionService:GetInstanceAddedSignal(long), CollectionService:GetInstanceRemovedSignal(long))
            door:RemoveTag(long)
            print(door:HasTag(long), #CollectionService:GetTagged(long))
            """);

        printed.ShouldBe(["true true Door", "Signal InstanceAdded Signal InstanceRemoved", "false 0"]);
    }

    [Theory]
    [InlineData("door:AddTag(long)", "AddTag was handed a tag of 257 bytes, and a tag is at most 256 bytes long")]
    [InlineData("tags:AddTag(door, long)", "AddTag was handed a tag of 257 bytes, and a tag is at most 256 bytes long")]
    [InlineData(
        "tags:GetInstanceAddedSignal(long)",
        "GetInstanceAddedSignal was handed a tag of 257 bytes, and a tag is at most 256 bytes long")]
    [InlineData(
        "tags:GetInstanceRemovedSignal(long)",
        "GetInstanceRemovedSignal was handed a tag of 257 bytes, and a tag is at most 256 bytes long")]
    [InlineData(
        "door:AddTag(string.rep('\\u{FC}', 129))",
        "AddTag was handed a tag of 258 bytes, and a tag is at most 256 bytes long")]
    public void A_longer_tag_is_refused_with_its_length(string call, string words)
    {
        using ScriptLevel level = House.With($"""
            local tags = game:GetService("CollectionService")
            local door = workspace.House.Door
            local long = string.rep("t", 257)
            {call}
            """);

        level.Play().Tick();

        level.Errors.ShouldHaveSingleItem().ShouldBe(words);
    }

    // As for bytes that are no text: no node has such a tag, so asking is no mistake.
    [Fact]
    public void A_longer_tag_is_one_no_node_has()
    {
        string[] printed = House.Ask(Service + """
            local long = string.rep("t", 100000)
            local door = workspace.House.Door
            door:RemoveTag(long)
            CollectionService:RemoveTag(door, long)
            print(door:HasTag(long), CollectionService:HasTag(door, long), #CollectionService:GetTagged(long), #door:GetTags())
            """);

        printed.ShouldBe(["false false 0 0"]);
    }

    [Fact]
    public void A_node_has_so_many_tags_and_takes_another_once_one_is_taken_off()
    {
        string[] printed = House.Ask(Service + """
            local lamp = workspace.House.Lamp
            for index = 1, 256 do
                lamp:AddTag("t" .. index)
            end
            print(select(2, pcall(lamp.AddTag, lamp, "t257")))
            print(select(2, pcall(CollectionService.AddTag, CollectionService, lamp, "t257")))
            lamp:AddTag("t1")
            lamp:RemoveTag("t2")
            lamp:AddTag("t257")
            print(#lamp:GetTags(), lamp:HasTag("t2"), lamp:HasTag("t257"))
            workspace.House.Sign:AddTag("t258")
            print(#CollectionService:GetTagged("t258"))
            """);

        printed[0].ShouldEndWith("\"Lamp\" has 256 tags, and a node has no more. RemoveTag takes one off");
        printed[1].ShouldEndWith("\"Lamp\" has 256 tags, and a node has no more. RemoveTag takes one off");
        printed[2..].ShouldBe(["256 false true", "1"]);
    }

    [Fact]
    public void The_words_of_AddTag_name_the_bounds_the_runtime_holds_it_to()
    {
        ScriptFunctionDescription add = LuauScriptApi.Description.FindClass("Instance").ShouldNotBeNull()
            .Methods.Single(method => method.Name == "AddTag");

        add.Summary.ShouldContain(
            $"A tag is at most {TagLimits.MaxBytes} bytes long, and a node has at most {TagLimits.MaxPerNode} tags.");
    }

    [Fact]
    public void A_tag_no_node_has_any_more_goes_when_the_tags_are_looked_over()
    {
        var table = new TagTable();
        var inLevel = new SceneNode("In");
        var outside = new SceneNode("Out");

        for (int round = 0; round < 1000; round++)
        {
            table.Add(inLevel, $"hit{round}", isInLevel: true);
            table.Remove(inLevel, $"hit{round}", out _);
        }

        table.TagCount.ShouldBe(1000);
        table.DropUnused().ShouldBe(0);
        table.TagCount.ShouldBe(0);

        // A node outside the level's tree has its tags too.
        table.Add(outside, "Kept", isInLevel: false);
        table.Add(inLevel, "Kept", isInLevel: true);
        table.Remove(inLevel, "Kept", out _);
        table.DropUnused();
        table.TagCount.ShouldBe(1);
        table.InOrder(table.Enter(outside).ShouldHaveSingleItem()).ShouldBe([outside]);

        table.Remove(outside, "Kept", out bool wasInLevel);
        wasInLevel.ShouldBeTrue();
        table.DropUnused();
        table.TagCount.ShouldBe(0);
    }

    [Fact]
    public void A_script_that_puts_on_and_takes_off_ever_new_tags_does_not_pile_them_up()
    {
        using ScriptLevel level = House.With("""
            local lamp = workspace.House.Lamp
            for round = 1, 5000 do
                lamp:AddTag("hit" .. round)
                lamp:RemoveTag("hit" .. round)
            end
            lamp:AddTag("Lit")
            print(lamp:HasTag("Lit"), #lamp:GetTags())
            """);

        level.Play().Tick();

        // The one it has, and those made since the tags were last looked over.
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true 1"]);
        level.Runtime.ShouldNotBeNull().KnownTagCount.ShouldBeInRange(1, 65);
    }

    [Fact]
    public void A_tag_whose_signal_a_script_holds_or_listens_to_stays_when_its_last_node_loses_it()
    {
        string[] printed = House.Ask(Service + """
            local lamp = workspace.House.Lamp
            local held = CollectionService:GetInstanceAddedSignal("Held")
            CollectionService:GetInstanceRemovedSignal("Heard"):Connect(function(node) print("heard", node.Name) end)
            for _, tag in { "Held", "Heard" } do
                lamp:AddTag(tag)
                lamp:RemoveTag(tag)
            end

            -- Many more tags are asked after, so the ones nobody wants are looked over.
            for round = 1, 2000 do
                CollectionService:GetInstanceAddedSignal("passing" .. round)
            end
            print(held == CollectionService:GetInstanceAddedSignal("Held"))
            held:Connect(function(node) print("held", node.Name) end)
            lamp:AddTag("Held")
            lamp:AddTag("Heard")
            lamp:RemoveTag("Heard")
            """);

        printed.ShouldBe(["true", "heard Lamp", "held Lamp", "heard Lamp"]);
    }

    [Fact]
    public void Tags_whose_signals_no_script_holds_or_listens_to_do_not_pile_up()
    {
        using ScriptLevel level = House.With(Service + """
            local kept = {}
            for round = 1, 200000 do
                local added = CollectionService:GetInstanceAddedSignal("passing" .. round)
                if round % 50000 == 0 then
                    table.insert(kept, added)
                end
            end
            for round = 1, 3 do
                CollectionService:GetInstanceRemovedSignal("heard" .. round):Connect(function() end)
            end
            print(#kept)
            """);

        level.Play().Tick();

        // The four it holds and the three it listens to stay, with those
        // asked for since the tags were last looked over.
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["4"]);
        level.Runtime.ShouldNotBeNull().KnownTagCount.ShouldBeInRange(7, 71);
    }
}
