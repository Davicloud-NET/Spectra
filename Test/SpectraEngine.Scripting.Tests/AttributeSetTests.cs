using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// SetAttribute and what reads it back: a value a script leaves on a node
/// under a name, for itself and for every other script.
/// </summary>
public sealed class AttributeSetTests
{
    [Fact]
    public void A_value_of_every_kind_reads_back_equal_to_what_was_set()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            local values = {
                Text = "Way out", Empty = "", Yes = true, No = false, Whole = 42, Part = 0.1, Minus = -1e300,
                Huge = math.huge, Point = Vector3.new(1, -2.5, 3), Frame = CFrame.new(1, 2, 3) * CFrame.Angles(0.1, 0.2, 0.3),
                Tint = Color3.fromRGB(255, 128, 0),
            }
            for name, value in values do
                door:SetAttribute(name, value)
            end
            local names = {}
            for name in values do table.insert(names, name) end
            table.sort(names)
            for _, name in names do
                local read = door:GetAttribute(name)
                print(name, typeof(read), read == values[name], rawequal(read, values[name]) or typeof(read))
            end
            print(door:GetAttribute("Part") + 1, door:GetAttribute("Point").Y, door:GetAttribute("Tint"):ToHex())
            """);

        printed.ShouldBe(
        [
            "Empty string true true",
            "Frame CFrame true CFrame",
            "Huge number true true",
            "Minus number true true",
            "No boolean true true",
            "Part number true true",
            "Point Vector3 true true",
            "Text string true true",
            "Tint Color3 true Color3",
            "Whole number true true",
            "Yes boolean true true",
            "1.1 -2.5 ff8000",
        ]);
    }

    [Fact]
    public void A_number_that_is_none_and_text_outside_ASCII_read_back_as_they_were_set()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            door:SetAttribute("Nothing", 0 / 0)
            door:SetAttribute("Sign", "T\u{FC}r \u{1F6AA}")
            local nothing = door:GetAttribute("Nothing")
            print(nothing ~= nothing, door:GetAttribute("Sign") == "T\u{FC}r \u{1F6AA}", #door:GetAttribute("Sign"))
            """);

        printed.ShouldBe(["true true 9"]);
    }

    [Fact]
    public void Nil_takes_an_attribute_away_and_a_name_nobody_set_is_nil()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            print(door:GetAttribute("Score"), door:GetAttribute(""), door:GetAttribute("not a name"))
            door:SetAttribute("Score", 3)
            door:SetAttribute("Score", nil)
            print(door:GetAttribute("Score"), next(door:GetAttributes()))
            door:SetAttribute("Score", 4)
            door:SetAttribute("Score")
            door:SetAttribute("Never", nil)
            print(door:GetAttribute("Score"), door:GetAttribute("Never"), select("#", door:SetAttribute("Score", 5)))
            """);

        printed.ShouldBe(["nil nil nil", "nil nil", "nil nil 0"]);
    }

    [Fact]
    public void A_later_set_takes_the_place_of_an_earlier_one_whatever_its_kind()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            door:SetAttribute("State", "shut")
            door:SetAttribute("State", 2)
            print(typeof(door:GetAttribute("State")), door:GetAttribute("State"))
            door:SetAttribute("State", Vector3.one)
            door:SetAttribute("State", false)
            print(typeof(door:GetAttribute("State")), door:GetAttribute("State"))
            """);

        printed.ShouldBe(["number 2", "boolean false"]);
    }

    [Fact]
    public void An_attribute_is_the_nodes_own_and_no_other_node_has_it()
    {
        string[] printed = House.Ask("""
            local door = workspace.House.Door
            door:SetAttribute("Score", 3)
            workspace:SetAttribute("Score", 9)
            script:SetAttribute("score", 1)
            print(door:GetAttribute("Score"), workspace:GetAttribute("Score"), door.Hinge:GetAttribute("Score"))
            print(script:GetAttribute("Score"), script:GetAttribute("score"), workspace.House.Lamp:GetAttribute("Score"))
            """);

        printed.ShouldBe(["3 9 nil", "nil 1 nil"]);
    }

    [Fact]
    public void GetAttributes_has_every_attribute_a_script_set_by_its_name()
    {
        string[] printed = House.Ask("""
            local lamp = workspace.House.Lamp
            print(next(lamp:GetAttributes()), lamp:GetAttributes() == lamp:GetAttributes())
            lamp:SetAttribute("On", true)
            lamp:SetAttribute("Watts", 60)
            lamp:SetAttribute("Gone", "soon")
            lamp:SetAttribute("Gone", nil)
            local all = lamp:GetAttributes()
            local names = {}
            for name, value in all do table.insert(names, name .. "=" .. tostring(value)) end
            table.sort(names)
            print(table.concat(names, " "))
            all.Watts = 100
            print(lamp:GetAttribute("Watts"), lamp:GetAttributes().Watts)
            """);

        // A table of its own each time: what a script does to one sets nothing.
        printed.ShouldBe(["nil false", "On=true Watts=60", "60 60"]);
    }

    [Fact]
    public void Two_scripts_share_one_attribute()
    {
        using var level = new ScriptLevel();
        level.Part("Board", new Vector3(3f, 1f, 3f));
        level.AddScript(level.Scene.Root, """
            workspace.Board:SetAttribute("Score", 1)
            task.wait()
            print("first reads", workspace.Board:GetAttribute("Score"))
            """, "First");
        level.AddScript(level.Scene.Root, """
            local board = workspace.Board
            print("second reads", board:GetAttribute("Score"))
            board:SetAttribute("Score", board:GetAttribute("Score") + 10)
            """, "Second");

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["second reads 1", "first reads 11"]);
    }

    [Theory]
    [InlineData("", "'' is not a valid attribute name")]
    [InlineData("my score", "'my score' is not a valid attribute name")]
    [InlineData("score:2", "'score:2' is not a valid attribute name")]
    [InlineData("a,b", "'a,b' is not a valid attribute name")]
    [InlineData("RBXScore", "'RBXScore' is not a valid attribute name")]
    [InlineData("\\255", "A name that is not valid UTF-8 is not a valid attribute name")]
    public void A_name_that_breaks_the_rule_is_refused_with_the_rule(string name, string words)
    {
        using ScriptLevel level = House.With($"workspace.House.Door:SetAttribute(\"{name}\", 1)");

        level.Play().Tick();

        level.Errors.ShouldHaveSingleItem().ShouldBe(
            $"{words}. An attribute's name is 1 to 100 letters, digits and any of _ . - /, and does not start with RBX");
    }

    // The letter by its code point: an escape in source does not survive every editing tool.
    [Fact]
    public void A_letter_outside_ASCII_is_no_letter_of_a_name()
    {
        using ScriptLevel level = House.With("workspace.House.Door:SetAttribute(\"T\\u{FC}r\", 1)");

        level.Play().Tick();

        level.Errors.ShouldHaveSingleItem().ShouldStartWith($"'T{(char)0xFC}r' is not a valid attribute name. ");
    }

    [Fact]
    public void A_name_of_a_hundred_characters_is_taken_and_one_more_is_refused()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            door:SetAttribute(string.rep("n", 100), 1)
            door:SetAttribute("rbx_lower_is_fine_9", 2)
            door:SetAttribute("_RBX", 3)
            print(door:GetAttribute(string.rep("n", 100)), door:GetAttribute("rbx_lower_is_fine_9"), door:GetAttribute("_RBX"))
            door:SetAttribute(string.rep("n", 101), 1)
            """);

        level.Play().Tick();

        level.Printed.ShouldBe(["1 2 3"]);
        level.Errors.ShouldHaveSingleItem().ShouldBe(
            "A name of 101 characters is not a valid attribute name. An attribute's name is 1 to 100 letters, " +
            "digits and any of _ . - /, and does not start with RBX");
    }

    [Theory]
    [InlineData("workspace", "Instance")]
    [InlineData("game", "DataModel")]
    [InlineData("{}", "table")]
    [InlineData("print", "function")]
    [InlineData("coroutine.running()", "thread")]
    [InlineData("workspace.House.Door.AttributeChanged", "RBXScriptSignal")]
    [InlineData("buffer.create(4)", "buffer")]
    public void A_value_no_attribute_holds_is_refused_with_the_kinds_one_holds(string value, string kind)
    {
        using ScriptLevel level = House.With($"""
            local door = workspace.House.Door
            door:SetAttribute("Kept", 7)
            print(pcall(door.SetAttribute, door, "Kept", {value}))
            print(door:GetAttribute("Kept"))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed[0].ShouldEndWith(
            $"{kind} is not a supported attribute type. An attribute holds a string, a boolean, a number, " +
            "a Vector3, a CFrame or a Color3, and nil takes it away");
        level.Printed[1].ShouldBe("7");
    }

    [Fact]
    public void Nothing_is_kept_for_a_node_whose_attributes_were_all_taken_away()
    {
        using ScriptLevel level = House.With("""
            local house = workspace.House
            for _, node in house:GetDescendants() do
                node:SetAttribute("Seen", true)
                node:SetAttribute("Note", "kept for a while")
            end
            task.wait()
            for _, node in house:GetDescendants() do
                node:SetAttribute("Seen", nil)
                node:SetAttribute("Note", nil)
            end
            house.Lamp:SetAttribute("Watts", 60)
            house.Door.AttributeChanged:Connect(function() end)
            """);

        level.Play().Tick();
        int held = level.Runtime.ShouldNotBeNull().AttributeNodeCount;
        level.Tick();

        level.Errors.ShouldBeEmpty();
        held.ShouldBeGreaterThan(5);
        level.Runtime.AttributeNodeCount.ShouldBe(2, "the lamp has an attribute and the door a signal");
    }

    [Theory]
    [InlineData("door:SetAttribute(5, 1)", "invalid argument #1 to 'SetAttribute' (string expected, got number)")]
    [InlineData("door:SetAttribute()", "invalid argument #1 to 'SetAttribute' (string expected, got nil)")]
    [InlineData("door:GetAttribute(door)", "invalid argument #1 to 'GetAttribute' (string expected, got Instance)")]
    [InlineData(
        "door:SetAttribute('Text', '\\255')",
        "A string that is not valid UTF-8 is not a supported attribute value. An attribute holds a string, a boolean, " +
        "a number, a Vector3, a CFrame or a Color3, and nil takes it away")]
    [InlineData("door.SetAttribute('Score', 1)", "Expected ':' not '.' calling member function SetAttribute")]
    [InlineData("door.GetAttributes()", "Expected ':' not '.' calling member function GetAttributes")]
    [InlineData("door.GetAttributes = nil", "Unable to assign property GetAttributes. Property is read only")]
    public void A_call_that_hands_over_the_wrong_thing_is_refused_with_these_words(string call, string words)
    {
        using ScriptLevel level = House.With($"""
            local door = workspace.House.Door
            {call}
            """);

        level.Play().Tick();

        level.Errors.ShouldHaveSingleItem().ShouldBe(words);
    }
}
