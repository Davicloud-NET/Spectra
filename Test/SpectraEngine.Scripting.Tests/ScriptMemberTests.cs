namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A script's way through each kind of member the generated binding has: a
/// method by a colon and by a dot, a property, an argument that is checked,
/// one that may be left out, and several results.
/// </summary>
public sealed class ScriptMemberTests
{
    private const string Values = """
        local a = Vector3.new(1, 2, 3)
        local f = CFrame.new(1, 2, 3)
        local run = game:GetService("RunService")
        """;

    [Fact]
    public void A_method_called_with_a_colon_and_one_read_and_then_called_do_the_same()
    {
        string[] printed = ScriptLevel.Run($$"""
            {{Values}}
            local lerp = f.Lerp
            print(f:Lerp(CFrame.new(3, 2, 1), 0.5) == lerp(f, CFrame.new(3, 2, 1), 0.5), f.Lerp == f.Lerp)
            print(a:Dot(a), a.Dot(a, a), a:Cross(a) == a.Cross(a, a))
            print(game.GetService(game, "RunService") == run, typeof(game.GetService))
            local connection = run.Heartbeat.Connect(run.Heartbeat, function() end)
            print(connection.Connected)
            connection.Disconnect(connection)
            print(connection.Connected)
            """);

        printed.ShouldBe(["true true", "14 14 true", "true function", "true", "false"]);
    }

    [Fact]
    public void A_method_read_and_called_without_its_object_says_to_use_a_colon()
    {
        string[] printed = ScriptLevel.Run($$"""
            {{Values}}
            print(pcall(function() return f.Inverse() end))
            print(pcall(function() return f.Inverse(a) end))
            print(pcall(function() return a.Dot() end))
            print(pcall(function() return game.GetService("RunService") end))
            print(pcall(function() return run.Heartbeat.Wait(game) end))
            """);

        printed[0].ShouldEndWith("Expected ':' not '.' calling member function Inverse");
        printed[1].ShouldEndWith("Expected ':' not '.' calling member function Inverse");
        printed[2].ShouldEndWith("Expected ':' not '.' calling member function Dot");
        printed[3].ShouldEndWith("Expected ':' not '.' calling member function GetService");
        printed[4].ShouldEndWith("Expected ':' not '.' calling member function Wait");
    }

    // Luau's own words: the property is read, and its value is no function.
    [Fact]
    public void A_property_called_like_a_method_is_a_call_of_its_value()
    {
        string[] printed = ScriptLevel.Run($$"""
            {{Values}}
            print(pcall(function() return script.Parent:Name() end))
            print(pcall(function() return workspace:Parent() end))
            print(pcall(function() return f:Position() end))
            print(pcall(function() return a:Magnitude() end))
            print(pcall(function() return run:Heartbeat() end))
            """);

        printed[0].ShouldEndWith("attempt to call a string value");
        printed[1].ShouldEndWith("attempt to call a nil value");
        printed[2].ShouldEndWith("attempt to call a Vector3 value");
        printed[3].ShouldEndWith("attempt to call a number value");
        printed[4].ShouldEndWith("attempt to call a RBXScriptSignal value");
    }

    [Fact]
    public void A_method_that_is_not_there_is_no_valid_member_with_a_colon_too()
    {
        string[] printed = ScriptLevel.Run($$"""
            {{Values}}
            print(pcall(function() return script.Parent:Destroy() end))
            print(pcall(function() return game:Destroy() end))
            print(pcall(function() return run.Heartbeat:Fire() end))
            print(pcall(function() return f:Destroy() end))
            print(pcall(function() return a:Destroy() end))
            """);

        printed[0].ShouldEndWith("Destroy is not a valid member of Part \"Part\"");
        printed[1].ShouldEndWith("Destroy is not a valid member of DataModel \"game\"");
        printed[2].ShouldEndWith("Fire is not a valid member of RBXScriptSignal \"Signal Heartbeat\"");
        printed[3].ShouldEndWith("Destroy is not a valid member of CFrame");
        printed[4].ShouldEndWith("Destroy is not a valid member of Vector3");
    }

    [Fact]
    public void An_argument_of_the_wrong_kind_names_its_place_and_what_was_wanted()
    {
        string[] printed = ScriptLevel.Run($$"""
            {{Values}}
            print(pcall(function() return f:Lerp(f, 'half') end))
            print(pcall(function() return f:FuzzyEq(f, {}) end))
            print(pcall(function() return f:PointToWorldSpace() end))
            print(pcall(function() return CFrame.lookAt(a) end))
            print(pcall(function() return CFrame.lookAt(a, a, 5) end))
            print(pcall(function() return CFrame.Angles(1, 2) end))
            print(pcall(function() return Vector3.new('x') end))
            print(pcall(function() return run.Heartbeat:Connect(5) end))
            """);

        printed[0].ShouldEndWith("invalid argument #2 to 'Lerp' (number expected, got string)");
        printed[1].ShouldEndWith("invalid argument #2 to 'FuzzyEq' (number expected, got table)");
        printed[2].ShouldEndWith("invalid argument #1 to 'PointToWorldSpace' (Vector3 expected, got nil)");
        printed[3].ShouldEndWith("invalid argument #2 to 'lookAt' (Vector3 expected, got nil)");
        printed[4].ShouldEndWith("invalid argument #3 to 'lookAt' (Vector3 expected, got number)");
        printed[5].ShouldEndWith("invalid argument #3 to 'Angles' (number expected, got nil)");
        printed[6].ShouldEndWith("invalid argument #1 to 'new' (number expected, got string)");
        printed[7].ShouldEndWith("invalid argument #1 to 'Connect' (function expected, got number)");
    }

    [Fact]
    public void An_argument_that_may_be_left_out_takes_nil_and_nothing_alike()
    {
        string[] printed = ScriptLevel.Run($$"""
            {{Values}}
            local b = Vector3.new(4, 5, 6)
            print(f:FuzzyEq(f), f:FuzzyEq(f, nil), f:FuzzyEq(f + Vector3.new(0, 0.05, 0), 0.1))
            print(CFrame.lookAt(a, b) == CFrame.lookAt(a, b, nil), CFrame.lookAt(a, b) == CFrame.lookAt(a, b, Vector3.yAxis))
            print(Vector3.new(nil, 2), Vector3.new(1, nil, 3))
            """);

        printed.ShouldBe(["true true true", "true true", "0, 2, 0 1, 0, 3"]);
    }

    [Fact]
    public void A_method_with_several_results_returns_them_all()
    {
        string[] printed = ScriptLevel.Run($$"""
            {{Values}}
            print(select('#', f:ToEulerAnglesXYZ()), select('#', f:ToOrientation()), select('#', f:GetComponents()))
            print(select('#', f:components()), f:components() == f:GetComponents())
            """);

        printed.ShouldBe(["3 3 12", "12 true"]);
    }

    // As everywhere in Luau, a string that reads as a number is taken for one.
    [Fact]
    public void A_string_that_reads_as_a_number_is_taken_for_a_number()
    {
        string[] printed = ScriptLevel.Run("""
            print(CFrame.Angles('0.5', 0, 0) == CFrame.Angles(0.5, 0, 0))
            """);

        printed.ShouldBe(["true"]);
    }
}
