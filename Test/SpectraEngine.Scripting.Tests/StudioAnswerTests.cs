using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Answers Roblox Studio 0.742 gave when it was asked on 2026-10-13, which
/// the engine gives too. Each test here states one of them, so a change
/// that leaves Studio's answer fails by its name.
/// </summary>
public sealed class StudioAnswerTests
{
    // Studio: typeof(workspace.ChildAdded) is RBXScriptSignal, typeof of a
    // connection is RBXScriptConnection, typeof(workspace) is Instance.
    [Fact]
    public void typeof_of_a_signal_is_RBXScriptSignal_and_of_a_connection_RBXScriptConnection_as_Studio_answers()
    {
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Echo").Entity = new EntityData(EchoEntity.Class);
        level.AddScript(level.Part("Part", new Vector3(0f, 2f, 0f)), """
            local run = game:GetService("RunService")
            local part = script.Parent
            local connection = run.Heartbeat:Connect(function() end)
            print(typeof(run.Heartbeat), typeof(part.Touched), typeof(workspace.Echo.OnPing))
            print(typeof(connection), typeof(part.Touched:Once(function() end)), typeof(workspace.Echo.OnPing:Connect(print)))
            print(typeof(workspace), typeof(game), typeof(run), typeof(part), typeof(script))
            print(tostring(run.Heartbeat), type(run.Heartbeat), type(connection))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "RBXScriptSignal RBXScriptSignal RBXScriptSignal",
            "RBXScriptConnection RBXScriptConnection RBXScriptConnection",
            "Instance Instance Instance Instance Instance",
            "Signal Heartbeat userdata userdata",
        ]);
    }

    // A signal and a connection are still what they were to everything that
    // takes one or is handed one where it does not belong.
    [Fact]
    public void A_signal_and_a_connection_work_as_before_whatever_typeof_says_of_them()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local run = game:GetService("RunService")
            local heartbeat = run.Heartbeat
            local beats = 0
            local connection = heartbeat.Connect(heartbeat, function() beats += 1 end)
            print(heartbeat == run.Heartbeat, connection.Connected, connection == connection)
            task.wait()
            connection.Disconnect(connection)
            print(beats, connection.Connected)
            print(select(2, pcall(function() return heartbeat.Connect(workspace, print) end)))
            print(select(2, pcall(function() return workspace:IsAncestorOf(heartbeat) end)))
            print(select(2, pcall(function() return workspace:IsAncestorOf(connection) end)))
            print(select(2, pcall(function() return heartbeat.Parent end)))
            print(select(2, pcall(function() connection.Connected = false end)))
            """);

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        level.Printed.Length.ShouldBe(7);
        level.Printed[0].ShouldBe("true true true");
        level.Printed[1].ShouldBe("1 false");
        level.Printed[2].ShouldEndWith("Expected ':' not '.' calling member function Connect");
        level.Printed[3].ShouldEndWith("invalid argument #1 to 'IsAncestorOf' (Instance expected, got RBXScriptSignal)");
        level.Printed[4].ShouldEndWith("invalid argument #1 to 'IsAncestorOf' (Instance expected, got RBXScriptConnection)");
        level.Printed[5].ShouldEndWith("Parent is not a valid member of RBXScriptSignal \"Signal Heartbeat\"");
        level.Printed[6].ShouldEndWith("Unable to assign property Connected. Property is read only");
    }

    // Studio: light.Brightness = -3 reads back 0, with no error.
    [Theory]
    [InlineData("-3")]
    [InlineData("-0.5")]
    [InlineData("'-2'")]
    public void A_brightness_below_zero_is_taken_as_zero_with_no_error_as_Studio_takes_it(string written)
    {
        using var level = new ScriptLevel();
        Light light = Lamp(level);
        level.AddScript(level.Scene.Root, $"""
            local lamp = workspace.Lamp
            lamp.Brightness = {written}
            print(lamp.Brightness)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["0"]);
        light.Intensity.ShouldBe(0f);

        level.Stop();
        light.Intensity.ShouldBe(1f);
    }

    // Studio takes these two as 0 as well. Here a brightness has to be a
    // number, and a light has to reach somewhere: the summaries say so.
    [Theory]
    [InlineData("lamp.Brightness = 0/0", "Brightness cannot be NaN: it takes a number that is not infinite.")]
    [InlineData("lamp.Brightness = -math.huge", "Brightness cannot be -Infinity: it takes a number that is not infinite.")]
    [InlineData("lamp.Range = -3", "Range cannot be -3: it takes a number above 0 that is not infinite.")]
    [InlineData("lamp.Range = 0", "Range cannot be 0: it takes a number above 0 that is not infinite.")]
    public void What_is_no_number_and_a_range_that_is_not_above_zero_are_still_refused(string write, string words)
    {
        using var level = new ScriptLevel();
        Light light = Lamp(level);
        level.AddScript(level.Scene.Root, $"local lamp = workspace.Lamp\nprint(select(2, pcall(function() {write} end)))");

        level.Play().Tick();

        level.Printed.ShouldHaveSingleItem().ShouldEndWith(words);
        (light.Intensity, light.Range).ShouldBe((1f, 10f));
        LuauScriptApiSummary("PointLight", "Range").ShouldContain("a light cannot have no reach");
        LuauScriptApiSummary("Light", "Brightness").ShouldContain("below 0 is taken as 0");
    }

    // Studio: Color3.toHSV(Color3.new(1, 0, 0)) gives 1 1 1, as the method does.
    [Theory]
    [InlineData("Color3.new(1, 0, 0)", "1 1 1")]
    [InlineData("Color3.new(0, 1, 0)", "0.3333 1 1")]
    [InlineData("Color3.new(0.5, 0.25, 0.25)", "1 0.5 0.5")]
    [InlineData("Color3.new(1, 0, 0.5)", "0.9167 1 1")]
    [InlineData("Color3.new(1, 0.5, 0)", "0.08333 1 1")]
    [InlineData("Color3.new(0.5, 0.5, 0.5)", "0 0 0.5")]
    public void Color3_toHSV_takes_a_colour_and_answers_as_the_method_does_and_as_Studio_answers(
        string colour, string hsv)
    {
        string[] printed = ScriptLevel.Run($"""
            local colour = {colour}
            local h, s, v = Color3.toHSV(colour)
            print(string.format("%.4g %.4g %.4g", h, s, v))
            local mh, ms, mv = colour:ToHSV()
            print(h == mh and s == ms and v == mv, select("#", Color3.toHSV(colour)))
            """);

        printed.ShouldBe([hsv, "true 3"]);
    }

    [Fact]
    public void Color3_toHSV_refuses_what_is_no_colour()
    {
        string[] printed = ScriptLevel.Run("""
            print(select(2, pcall(function() return Color3.toHSV(Vector3.one) end)))
            print(select(2, pcall(function() return Color3.toHSV() end)))
            """);

        printed[0].ShouldEndWith("invalid argument #1 to 'toHSV' (Color3 expected, got Vector3)");
        printed[1].ShouldEndWith("invalid argument #1 to 'toHSV' (Color3 expected, got nil)");
    }

    // Studio: SetAttribute takes a.b, a-b, a/b, rbxa, 9a, _ and a name of
    // 100 characters. It refuses "a b", the empty name, RBXa and a name of
    // 101 characters, and GetAttribute of a refused name is nil with no error.
    [Fact]
    public void An_attributes_name_takes_a_period_a_hyphen_a_slash_and_a_digit_in_front_as_Studio_takes_them()
    {
        string[] printed = ScriptLevel.Run("""
            local part = script.Parent
            local taken = { "a.b", "a-b", "a/b", "rbxa", "9a", "_", string.rep("n", 100) }
            for index, name in taken do
                part:SetAttribute(name, index)
            end
            local read = {}
            for index, name in taken do
                read[index] = tostring(part:GetAttribute(name))
            end
            print(table.concat(read, " "))
            local all = part:GetAttributes()
            print(all["a.b"], all["a-b"], all["a/b"])
            for _, name in { "a b", "", "RBXa", string.rep("n", 101) } do
                print((pcall(part.SetAttribute, part, name, 1)), part:GetAttribute(name))
            end
            """);

        printed.ShouldBe(["1 2 3 4 5 6 7", "1 2 3", "false nil", "false nil", "false nil", "false nil"]);
    }

    // Studio: GetTags after AddTag of b, a and c gives a b c.
    [Fact]
    public void GetTags_hands_the_tags_back_sorted_and_not_in_the_order_they_were_added_as_Studio_does()
    {
        string[] printed = ScriptLevel.Run("""
            local CollectionService = game:GetService("CollectionService")
            local part = script.Parent
            part:AddTag("b")
            part:AddTag("a")
            CollectionService:AddTag(part, "c")
            print(table.concat(part:GetTags(), " "), table.concat(CollectionService:GetTags(part), " "))
            """);

        printed.ShouldBe(["a b c a b c"]);
    }

    // Studio: a destroyed part keeps its tags and attributes. HasTag is true
    // and GetAttribute gives 1, and AddTag and SetAttribute are still taken.
    [Fact]
    public void A_destroyed_part_keeps_its_tags_and_attributes_and_takes_new_ones_with_no_error_as_in_Studio()
    {
        string[] printed = ScriptLevel.Run("""
            local part = Instance.new("Part", workspace)
            part:AddTag("T")
            part:SetAttribute("N", 1)
            part:Destroy()
            print(part:HasTag("T"), part:GetAttribute("N"), table.concat(part:GetTags(), " "), part:GetAttributes().N)
            print(pcall(function()
                part:AddTag("U")
                part:SetAttribute("M", 2)
            end))
            print(part:HasTag("U"), part:GetAttribute("M"), table.concat(part:GetTags(), " "))
            """);

        printed.ShouldBe(["true 1 T 1", "true", "true 2 T U"]);
    }

    private static Light Lamp(ScriptLevel level) =>
        level.Scene.Root.CreateChild("Lamp").Light = new Light { Kind = LightKind.Point };

    private static string LuauScriptApiSummary(string className, string property) =>
        SpectraEngine.Scripting.Luau.Runtime.LuauScriptApi.Description
            .FindClass(className).ShouldNotBeNull()
            .Properties.Single(described => described.Name == property).Summary;
}
