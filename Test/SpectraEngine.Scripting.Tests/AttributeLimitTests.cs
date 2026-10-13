using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// How much scripts can leave on a node as attributes. It is kept outside
/// the memory a script's budget counts, so it has bounds of its own.
/// </summary>
public sealed class AttributeLimitTests
{
    [Fact]
    public void A_string_of_the_longest_length_is_taken_and_a_longer_one_is_refused()
    {
        string[] printed = House.Ask("""
            local lamp = workspace.House.Lamp
            lamp:SetAttribute("Note", string.rep("x", 65536))
            print(#lamp:GetAttribute("Note"))
            print(select(2, pcall(lamp.SetAttribute, lamp, "Note", string.rep("x", 65537))))
            print(select(2, pcall(lamp.SetAttribute, lamp, "Other", string.rep("x", 1e6))))
            print(#lamp:GetAttribute("Note"), lamp:GetAttribute("Other"))
            """);

        printed[0].ShouldBe("65536");
        printed[1].ShouldEndWith(
            "A string of 65537 bytes is not a supported attribute value. An attribute's string is at most 65536 bytes long");
        printed[2].ShouldEndWith(
            "A string of 1000000 bytes is not a supported attribute value. An attribute's string is at most 65536 bytes long");
        printed[3].ShouldBe("65536 nil");
    }

    // The length is counted the way Luau's # counts it.
    [Fact]
    public void The_length_of_a_string_is_counted_in_bytes()
    {
        string[] printed = House.Ask("""
            local lamp = workspace.House.Lamp
            local letter = "\u{FC}"
            print(pcall(lamp.SetAttribute, lamp, "Fits", string.rep(letter, 32768)))
            print((pcall(lamp.SetAttribute, lamp, "Long", string.rep(letter, 32769))))
            """);

        printed.ShouldBe(["true", "false"]);
    }

    [Fact]
    public void A_node_holds_so_many_attributes_and_takes_another_once_one_is_taken_away()
    {
        string[] printed = House.Ask("""
            local lamp = workspace.House.Lamp
            for index = 1, 256 do
                lamp:SetAttribute("k" .. index, index)
            end
            print(select(2, pcall(lamp.SetAttribute, lamp, "k257", 257)))
            lamp:SetAttribute("k1", "set again")
            lamp:SetAttribute("k2", nil)
            lamp:SetAttribute("k257", 257)
            print(lamp:GetAttribute("k1"), lamp:GetAttribute("k2"), lamp:GetAttribute("k257"))
            workspace.House.Sign:SetAttribute("k258", 258)
            print(workspace.House.Sign:GetAttribute("k258"))
            """);

        printed[0].ShouldEndWith(
            "\"Lamp\" has 256 attributes, and a node holds no more. SetAttribute with nil takes one away");
        printed[1..].ShouldBe(["set again nil 257", "258"]);
    }

    [Fact]
    public void A_node_keeps_the_signals_of_so_many_names_and_no_more()
    {
        string[] printed = House.Ask("""
            local lamp = workspace.House.Lamp
            local first = lamp:GetAttributeChangedSignal("k1")
            for index = 2, 256 do
                lamp:GetAttributeChangedSignal("k" .. index)
            end
            print(select(2, pcall(lamp.GetAttributeChangedSignal, lamp, "k257")))
            print(first == lamp:GetAttributeChangedSignal("k1"), workspace.House.Sign:GetAttributeChangedSignal("k257") ~= nil)
            """);

        printed[0].ShouldEndWith(
            "\"Lamp\" keeps the signals of 256 attribute names, and a node keeps no more");
        printed[1].ShouldBe("true true");
    }

    [Fact]
    public void Names_that_were_set_and_taken_away_again_do_not_pile_up()
    {
        var table = new AttributeTable();
        var node = new SceneNode("Node");
        table.Set(node, table.NameOf("Kept"), MemberValue.Of(1d));

        for (int round = 0; round < 10_000; round++)
        {
            string name = table.NameOf($"k{round}");
            table.Set(node, name, MemberValue.Of(round));
            table.Forget(node, name);
        }

        table.NameCount.ShouldBeLessThan(200);
        table.TryGet(node, "Kept", out MemberValue kept).ShouldBeTrue();
        kept.Number.ShouldBe(1d);
        table.NameOf("Kept").ShouldBeSameAs(table.NameOf("Kept"));
    }

    [Fact]
    public void The_words_of_SetAttribute_name_the_bounds_the_runtime_holds_it_to()
    {
        ScriptFunctionDescription set = LuauScriptApi.Description.FindClass("Instance").ShouldNotBeNull()
            .Methods.Single(method => method.Name == "SetAttribute");

        set.Summary.ShouldContain(
            $"A string is at most {AttributeLimits.MaxTextBytes} bytes long, " +
            $"and a node holds at most {AttributeLimits.MaxPerNode} attributes.");
    }

    // A loop that sets a new name every round stops at the node's bound.
    [Fact]
    public void A_script_that_sets_ever_new_names_is_stopped_with_an_error_it_can_read()
    {
        using ScriptLevel level = House.With("""
            local lamp = workspace.House.Lamp
            local round = 0
            while true do
                round += 1
                lamp:SetAttribute("k" .. round, string.rep("x", 1000))
            end
            """);

        level.Play().Tick();

        level.Errors.ShouldHaveSingleItem().ShouldBe(
            "\"Lamp\" has 256 attributes, and a node holds no more. SetAttribute with nil takes one away");
    }
}
