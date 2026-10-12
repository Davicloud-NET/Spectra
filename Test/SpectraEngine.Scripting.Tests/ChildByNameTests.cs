using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A child read as if it were a member: <c>workspace.Door</c>. A member of
/// that name always wins, and a name that is neither is an error.
/// </summary>
public sealed class ChildByNameTests
{
    [Fact]
    public void A_name_that_is_no_member_gives_the_child_of_that_name()
    {
        string[] printed = House.Ask("""
            print(workspace.House, workspace.House.Door, workspace.House.Door.Hinge.Name)
            print(workspace.House == workspace:FindFirstChild("House"), workspace["House"] == workspace.House)
            print(workspace.House.Lamp.Shade.ClassName, workspace.Rules.ClassName, workspace.Main == script)
            """);

        printed.ShouldBe(["House Door Hinge", "true true", "SpotLight ModuleScript true"]);
    }

    [Fact]
    public void A_member_wins_over_a_child_of_the_same_name()
    {
        using var level = new ScriptLevel();
        SceneNode shelf = level.Scene.Root.CreateChild("Shelf");
        foreach (string name in new[] { "Name", "Parent", "ClassName", "Position", "FindFirstChild", "GetChildren" })
            shelf.CreateChild(name);

        level.AddScript(level.Scene.Root, """
            local shelf = workspace.Shelf
            print(shelf.Name, shelf.Parent, shelf.ClassName, typeof(shelf.Position))
            print(type(shelf.FindFirstChild), #shelf:GetChildren())
            print(shelf:FindFirstChild("Name").ClassName, shelf:FindFirstChild("Parent").Parent)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Shelf Root Folder Vector3", "function 6", "Folder Shelf"]);
    }

    [Fact]
    public void A_child_whose_name_has_a_space_or_is_a_number_is_read_with_brackets()
    {
        using var level = new ScriptLevel();
        SceneNode shelf = level.Scene.Root.CreateChild("Shelf");
        shelf.CreateChild("Front Door");
        shelf.CreateChild("5");
        shelf.CreateChild("1.5");
        level.AddScript(level.Scene.Root, """
            local shelf = workspace.Shelf
            print(shelf["Front Door"], shelf["5"], shelf["1.5"])
            print(shelf:FindFirstChild("Front Door"), shelf:FindFirstChild("5"), shelf:FindFirstChild("1.5"))
            print(pcall(function() return shelf[5] end))
            print(pcall(function() return shelf["Front"] end))
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "Front Door 5 1.5",
            "Front Door 5 1.5",
            $"false {here}:4: a member is named by a string, not by a number",
            $"false {here}:5: Front is not a valid member of Folder \"Shelf\"",
        ]);
    }

    [Theory]
    [InlineData("workspace.Nobody", "Nobody is not a valid member of Folder \"Root\"")]
    [InlineData("workspace.House.Walls.North.Hinge", "Hinge is not a valid member of Part \"North\"")]
    [InlineData("workspace.House.Door.Anchored", "Anchored is not a valid member of sayer \"Door\"")]
    [InlineData("workspace.House.Lamp.Bulb", "Bulb is not a valid member of PointLight \"Lamp\"")]
    [InlineData("workspace.house", "house is not a valid member of Folder \"Root\"")]
    [InlineData("script.Parent.House.Sign.Door", "Door is not a valid member of MeshPart \"Sign\"")]
    [InlineData("game.House", "House is not a valid member of DataModel \"game\"")]
    public void A_name_that_is_neither_a_member_nor_a_child_is_an_error_that_names_the_node(string read, string error)
    {
        using ScriptLevel level = House.With($"local _ = {read}");

        level.Play().Tick();

        level.Errors.ShouldBe([error]);
    }

    [Theory]
    [InlineData("workspace.House = workspace.Rules", "House is not a valid member of Folder \"Root\"")]
    [InlineData("workspace:House()", "attempt to call a Instance value")]
    [InlineData("workspace:Nobody()", "Nobody is not a valid member of Folder \"Root\"")]
    public void A_child_cannot_be_written_or_called_by_its_name(string statement, string error)
    {
        using ScriptLevel level = House.With(statement);

        level.Play().Tick();

        level.Errors.ShouldBe([error]);
    }

    [Fact]
    public void Of_two_children_with_one_name_the_first_in_the_tree_is_the_one()
    {
        using var level = new ScriptLevel();
        SceneNode shelf = level.Scene.Root.CreateChild("Shelf");
        shelf.CreateChild("Twin").CreateChild("First");
        shelf.CreateChild("Twin").CreateChild("Second");
        level.AddScript(level.Scene.Root, """
            local shelf = workspace.Shelf
            print(shelf.Twin:GetChildren()[1], shelf:FindFirstChild("Twin"):GetChildren()[1])
            print(shelf:GetChildren()[2]:GetChildren()[1])
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["First First", "Second"]);
    }

    [Fact]
    public void A_child_is_read_by_the_name_it_has_now()
    {
        using ScriptLevel level = House.With("""
            local house = workspace.House
            local door = house.Door
            door.Name = "Gate"
            print(house.Gate == door, house:FindFirstChild("Door"), pcall(function() return house.Door end))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldHaveSingleItem().ShouldStartWith("true nil false ");

        level.Stop();
        level.Scene.Root.Traverse().ShouldContain(node => node.Name == "Door");
    }
}
