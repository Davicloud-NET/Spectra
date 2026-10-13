using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Instance.new: the classes it makes, what a new node of each is, and what
/// it says of a class it cannot make.
/// </summary>
public sealed class InstanceNewTests
{
    [Fact]
    public void A_new_node_is_of_its_class_named_after_it_and_in_no_level()
    {
        string[] printed = House.Ask("""
            for _, class in { "Part", "Folder", "PointLight", "SpotLight", "SurfaceLight", "DirectionalLight", "DiscLight" } do
                local made = Instance.new(class)
                print(made.ClassName, made.Name, made.Parent, made:IsDescendantOf(game), made:IsA(class), typeof(made), #made:GetChildren())
            end
            """);

        printed.ShouldBe(
        [
            "Part Part nil false true Instance 0",
            "Folder Folder nil false true Instance 0",
            "PointLight PointLight nil false true Instance 0",
            "SpotLight SpotLight nil false true Instance 0",
            "SurfaceLight SurfaceLight nil false true Instance 0",
            "DirectionalLight DirectionalLight nil false true Instance 0",
            "DiscLight DiscLight nil false true Instance 0",
        ]);
    }

    [Fact]
    public void A_new_part_is_solid_drawn_and_at_the_origin_and_a_new_light_has_Robloxs_numbers()
    {
        string[] printed = House.Ask("""
            local part = Instance.new("Part")
            print(part.Position, part.CanCollide, part.CanQuery, part.CanTouch, part.Visible, part:IsA("BasePart"))
            local lamp = Instance.new("PointLight")
            print(lamp.Brightness, lamp.Range, lamp.Color, lamp.Enabled, lamp:IsA("Light"))
            print(Instance.new("SpotLight").Range, Instance.new("SurfaceLight").Range, Instance.new("DiscLight").Range)
            """);

        printed.ShouldBe(["0, 0, 0 true true true true true", "1 8 1, 1, 1 true true", "16 16 16"]);
    }

    [Fact]
    public void A_new_part_is_in_the_level_where_it_was_put_once_it_has_a_parent()
    {
        using ScriptLevel level = House.With("""
            local part = Instance.new("Part")
            part.Name = "Crate"
            part.Position = Vector3.new(3, 2, 1)
            part.Parent = workspace.House
            print(part.Parent, part:IsDescendantOf(game), workspace.House.Crate == part, part:GetFullName())
            print(workspace.House:GetChildren()[#workspace.House:GetChildren()] == part)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["House true true Root.House.Crate", "true"]);
        SceneNode crate = level.Scene.Root.Children.Single(node => node.Name == "House").Children[^1];
        crate.WorldPosition.ShouldBe(new Vector3(3f, 2f, 1f));
        level.Scene.DrawableNodes.ShouldContain(crate);
        level.Scene.TryFindById(crate.Id, out _).ShouldBeTrue();
    }

    [Fact]
    public void A_second_argument_is_the_parent_as_in_Roblox()
    {
        string[] printed = House.Ask("""
            local lamp = Instance.new("PointLight", workspace.House.Lamp)
            print(lamp.Parent, lamp:IsDescendantOf(game))
            print(pcall(Instance.new, "Part", 5))
            print(pcall(Instance.new, "Part", game))
            """);

        printed.ShouldBe(
        [
            "Lamp true",
            "false Scripts/Main1.luau:3: invalid argument #2 to 'new' (Instance expected, got number)",
            "false Scripts/Main1.luau:4: invalid argument #2 to 'new' (Instance expected, got DataModel)",
        ]);
    }

    [Fact]
    public void A_node_that_cannot_have_the_parent_it_was_made_for_is_not_made()
    {
        using ScriptLevel level = House.With("""
            local gone = Instance.new("Folder", workspace)
            gone:Destroy()
            print(pcall(Instance.new, "Part", gone))
            """);

        level.Play().Tick();

        level.Printed.ShouldBe(["false Scripts/Main1.luau:3: Part cannot get Folder as its parent: Folder was destroyed"]);
        new ScriptWrites(level.Entities).Tree.MadeCount.ShouldBe(0);
        level.Runtime.ShouldNotBeNull().LooseTreeCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("MeshPart")]
    [InlineData("Script")]
    [InlineData("Model")]
    [InlineData("func_door")]
    [InlineData("Instance")]
    public void A_class_it_cannot_make_is_an_error_that_says_what_it_makes(string className)
    {
        using ScriptLevel level = House.With($"Instance.new('{className}')");

        level.Play().Tick();

        level.Sink.Messages.ShouldHaveSingleItem().Line.ShouldBe(1);
        level.Errors.ShouldBe(
        [
            $"Unable to create an Instance of type \"{className}\". Instance.new makes a Part, a Folder, " +
            "a PointLight, a SpotLight, a SurfaceLight, a DirectionalLight or a DiscLight. " +
            "Clone a node of the level for anything else",
        ]);
    }

    [Fact]
    public void What_is_no_class_name_is_refused_in_Robloxs_words()
    {
        string[] printed = House.Ask("""
            print(pcall(Instance.new))
            print(pcall(Instance.new, workspace))
            print(pcall(Instance.new, "\xff\xfe"))
            """);

        printed.ShouldBe(
        [
            "false Scripts/Main1.luau:1: invalid argument #1 to 'new' (string expected, got nil)",
            "false Scripts/Main1.luau:2: invalid argument #1 to 'new' (string expected, got Instance)",
            "false Scripts/Main1.luau:3: Instance.new was handed a class name that is not valid UTF-8",
        ]);
    }

    [Fact]
    public void Stop_takes_away_every_node_a_script_made()
    {
        using ScriptLevel level = House.With("""
            for i = 1, 5 do
                local part = Instance.new("Part", workspace)
                Instance.new("PointLight", part)
            end
            """);
        var before = new LevelState(level.Scene);
        int built = level.Scene.NodeCount;

        level.Play().Tick();
        level.Scene.NodeCount.ShouldBe(built + 10);
        level.Stop();

        level.Scene.NodeCount.ShouldBe(built);
        new LevelState(level.Scene).ShouldBe(before, "after Instance.new");
    }

    // The summary a person reads in the editor has the numbers in it.
    [Fact]
    public void The_summary_of_new_names_the_bound_and_the_defaults_the_engine_has()
    {
        ScriptFunctionDescription made = LuauScriptApi.Description.Libraries
            .Single(library => library.Name == "Instance").Functions.Single(function => function.Name == "new");

        made.Summary.ShouldContain($"at most {TreeLimits.MaxMadeNodes} nodes");
        made.Summary.ShouldContain($"a Range of {MadeNodes.PointLightRange}");
        made.Summary.ShouldContain($"or {MadeNodes.BeamLightRange} for");
        made.Summary.ShouldContain("a solid cube one unit to a side");
        MadeNodes.PartSize.ShouldBe(1f);
    }
}
