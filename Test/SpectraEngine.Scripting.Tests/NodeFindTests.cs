using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// How a script finds other nodes: by name or class below a node and above
/// it, a node's children and everything under it, and where a node sits.
/// </summary>
public sealed class NodeFindTests
{
    [Fact]
    public void A_child_is_found_by_its_name_and_a_name_no_child_has_gives_nil()
    {
        string[] printed = House.Ask("""
            local house = workspace:FindFirstChild("House")
            print(house, house:FindFirstChild("Door"), house.Walls:FindFirstChild("South"))
            print(workspace:FindFirstChild("Hinge"), workspace:FindFirstChild("house"), workspace:FindFirstChild(""))
            print(house:FindFirstChild("Door") == house.Door, house:FindFirstChild("Door", false) == house.Door)
            """);

        printed.ShouldBe(["House Door South", "nil nil nil", "true true"]);
    }

    // Tree.A.Target comes first in the tree's order, and Tree.Target is the
    // one a search from Tree gives.
    [Fact]
    public void A_recursive_find_looks_at_a_nodes_children_before_what_is_below_them()
    {
        using var level = new ScriptLevel();
        SceneNode tree = level.Scene.Root.CreateChild("Tree");
        tree.CreateChild("A").CreateChild("Target");
        tree.CreateChild("Target");
        level.AddScript(level.Scene.Root, """
            local tree = workspace.Tree
            print(tree:FindFirstChild("Target", true):GetFullName())
            print(workspace:FindFirstChild("Target", true):GetFullName())
            print(workspace:FindFirstChild("Target"), workspace:FindFirstChild("Nobody", true))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Root.Tree.Target", "Root.Tree.Target", "nil nil"]);
    }

    // Grove.C.Target is nearer to Grove. The search goes down under B first.
    [Fact]
    public void A_recursive_find_goes_all_the_way_down_under_one_child_before_it_looks_under_the_next()
    {
        using var level = new ScriptLevel();
        SceneNode grove = level.Scene.Root.CreateChild("Grove");
        grove.CreateChild("B").CreateChild("X").CreateChild("Target");
        grove.CreateChild("C").CreateChild("Target");
        level.AddScript(level.Scene.Root, """
            print(workspace.Grove:FindFirstChild("Target", true):GetFullName())
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Root.Grove.B.X.Target"]);
    }

    [Fact]
    public void A_find_by_class_takes_the_class_name_as_it_is_and_no_class_that_extends_it()
    {
        string[] printed = House.Ask("""
            local house = workspace.House
            print(house.Walls:FindFirstChildOfClass("Part"), house:FindFirstChildOfClass("Part"))
            print(house:FindFirstChildOfClass("sayer"), house:FindFirstChildOfClass("PointLight"))
            print(house:FindFirstChildOfClass("BasePart"), house:FindFirstChildOfClass("Light"))
            print(workspace:FindFirstChildOfClass("ModuleScript"), workspace:FindFirstChildOfClass("part"))
            """);

        printed.ShouldBe(["North nil", "Door Lamp", "nil nil", "Rules nil"]);
    }

    [Fact]
    public void A_find_by_what_a_node_is_takes_the_classes_its_class_extends_too()
    {
        string[] printed = House.Ask("""
            local house = workspace.House
            print(house:FindFirstChildWhichIsA("BasePart"), house:FindFirstChildWhichIsA("Light"))
            print(house:FindFirstChildWhichIsA("Instance"), house:FindFirstChildWhichIsA("MeshPart"))
            print(house:FindFirstChildWhichIsA("SpotLight"), house:FindFirstChildWhichIsA("SpotLight", true))
            print(workspace:FindFirstChildWhichIsA("MeshPart", true), workspace:FindFirstChildWhichIsA("Model", true))
            """);

        printed.ShouldBe(["Door Lamp", "Walls Sign", "nil Shade", "Sign nil"]);
    }

    [Fact]
    public void An_ancestor_is_found_by_name_by_class_and_by_what_it_is_from_the_nearest_up()
    {
        string[] printed = House.Ask("""
            local hinge = workspace.House.Door.Hinge
            print(hinge:FindFirstAncestor("House"), hinge:FindFirstAncestor("Root"), hinge:FindFirstAncestor("Door"))
            print(hinge:FindFirstAncestor("Hinge"), hinge:FindFirstAncestor("Walls"), workspace:FindFirstAncestor("Root"))
            print(hinge:FindFirstAncestorOfClass("Folder"), hinge:FindFirstAncestorOfClass("sayer"))
            print(hinge:FindFirstAncestorOfClass("Part"), hinge:FindFirstAncestorOfClass("Instance"))
            print(hinge:FindFirstAncestorWhichIsA("Part"), hinge:FindFirstAncestorWhichIsA("BasePart"))
            print(hinge:FindFirstAncestorWhichIsA("Folder"), hinge:FindFirstAncestorWhichIsA("Instance"))
            print(hinge:FindFirstAncestorWhichIsA("Light"), workspace.House.Lamp.Shade:FindFirstAncestorWhichIsA("Light"))
            """);

        printed.ShouldBe(
        [
            "House Root Door",
            "nil nil nil",
            "House Door",
            "nil nil",
            "Door Door",
            "House Door",
            "nil Lamp",
        ]);
    }

    [Fact]
    public void GetChildren_gives_the_children_in_the_order_of_the_tree_as_a_table_of_its_own()
    {
        string[] printed = House.Ask("""
            local function names(nodes)
                local list = {}
                for index, node in nodes do list[index] = node.Name end
                return table.concat(list, " ")
            end

            print(names(workspace:GetChildren()))
            print(names(workspace.House:GetChildren()))
            print(#workspace.House.Door.Hinge:GetChildren())

            local children = workspace.House:GetChildren()
            table.remove(children, 1)
            print(#children, #workspace.House:GetChildren(), children[1] == workspace.House.Door)
            """);

        printed.ShouldBe(
        [
            "Sun Baseplate House Panel Dish Relay Rules Main",
            "Walls Door Lamp Sign",
            "0",
            "3 4 true",
        ]);
    }

    [Fact]
    public void GetDescendants_gives_everything_below_with_each_parent_before_its_children()
    {
        string[] printed = House.Ask("""
            local function names(nodes)
                local list = {}
                for index, node in nodes do list[index] = node.Name end
                return table.concat(list, " ")
            end

            print(names(workspace.House:GetDescendants()))
            print(#workspace:GetDescendants(), #workspace.House.Sign:GetDescendants())
            """);

        printed.ShouldBe(["Walls North South Door Hinge Lamp Shade Sign", "16 0"]);
    }

    [Fact]
    public void A_node_is_above_everything_below_it_and_neither_above_nor_below_itself()
    {
        string[] printed = House.Ask("""
            local house, hinge = workspace.House, workspace.House.Door.Hinge
            print(house:IsAncestorOf(hinge), workspace:IsAncestorOf(hinge), hinge:IsAncestorOf(house))
            print(hinge:IsDescendantOf(house), hinge:IsDescendantOf(workspace), house:IsDescendantOf(hinge))
            print(house:IsAncestorOf(house), house:IsDescendantOf(house))
            print(house.Walls:IsAncestorOf(hinge), hinge:IsDescendantOf(house.Walls))
            """);

        printed.ShouldBe(["true true false", "true true false", "false false", "false false"]);
    }

    // Roblox's own sample for IsDescendantOf asks a part about game.
    [Fact]
    public void Every_node_of_the_level_is_below_game_and_game_is_below_no_node()
    {
        string[] printed = House.Ask("""
            local hinge = workspace.House.Door.Hinge
            print(hinge:IsDescendantOf(game), workspace:IsDescendantOf(game), script:IsDescendantOf(game))
            print(hinge:IsAncestorOf(game), workspace:IsAncestorOf(game))
            """);

        printed.ShouldBe(["true true true", "false false"]);
    }

    [Fact]
    public void A_node_that_left_the_level_is_no_longer_below_game()
    {
        using var level = new ScriptLevel();
        SceneNode crate = level.Scene.Root.CreateChild("Crate");
        crate.CreateChild("Lid");
        level.AddScript(level.Scene.Root, """
            local crate, lid = workspace.Crate, workspace.Crate.Lid
            print("removing")
            print(crate:IsDescendantOf(game), lid:IsDescendantOf(game), lid:IsDescendantOf(crate), crate.Parent)
            """);
        level.Sink.Heard = message =>
        {
            if (message.Text == "removing")
                level.Scene.Root.RemoveChild(crate);
        };

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["removing", "false false true nil"]);
    }

    // Roblox's rule. A script that asks about a node it did not find gets true.
    [Fact]
    public void Every_node_is_below_nil_and_nil_is_below_no_node()
    {
        string[] printed = House.Ask("""
            local hinge, nobody = workspace.House.Door.Hinge, workspace:FindFirstChild("Nobody")
            print(hinge:IsDescendantOf(nil), workspace:IsDescendantOf(nobody))
            print(hinge:IsAncestorOf(nil), workspace:IsAncestorOf(nobody))
            """);

        printed.ShouldBe(["true true", "false false"]);
    }

    [Fact]
    public void The_full_name_is_every_name_from_the_root_down_with_dots_between()
    {
        string[] printed = House.Ask("""
            print(workspace.House.Door.Hinge:GetFullName())
            print(workspace:GetFullName(), script:GetFullName())
            """);

        printed.ShouldBe(["Root.House.Door.Hinge", "Root Root.Main"]);
    }

    // The escape and not the letter: an editing tool may not keep it.
    [Fact]
    public void A_name_outside_ASCII_is_found_as_it_was_written()
    {
        string door = $"T{(char)0xFC}r";
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild(door).CreateChild("Leaf");
        level.AddScript(level.Scene.Root, $$"""
            local door = workspace:FindFirstChild("{{door}}")
            print(door == workspace["{{door}}"], door.Leaf:FindFirstAncestor("{{door}}") == door)
            print(workspace:FindFirstChild("Tur"), #door.Name, door.Leaf:GetFullName() == "Root.{{door}}.Leaf")
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true true", "nil 4 true"]);
    }

    [Fact]
    public void Bytes_that_are_no_text_name_no_node_and_no_class()
    {
        string[] printed = House.Ask("""
            local house = workspace.House
            print(house:FindFirstChild("\xff"), house:FindFirstChild("Do\xffor", true), house:FindFirstAncestor("\xc3"))
            print(house:IsA("\xff"), house:FindFirstChildOfClass("\xff"), house:FindFirstChildWhichIsA("\xff"))
            print(pcall(function() return house["\xff"] end))
            """);

        printed[0].ShouldBe("nil nil nil");
        printed[1].ShouldBe("false nil nil");
        printed[2].ShouldStartWith("false ");
        printed[2].ShouldEndWith(" is not a valid member of Folder \"House\"");
    }

    // The editor lets a name be longer than a running level may give one.
    [Fact]
    public void A_name_longer_than_a_running_level_may_give_is_still_found()
    {
        string name = new('n', 700);
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild(name);
        level.AddScript(level.Scene.Root, $$"""
            local long = string.rep("n", 700)
            print(workspace:FindFirstChild(long) == workspace[long], #workspace:FindFirstChild(long).Name)
            print(workspace:FindFirstChild(string.rep("n", 699)), workspace:FindFirstChild(string.rep("n", 701)))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true 700", "nil nil"]);
    }

    [Theory]
    [InlineData("workspace:FindFirstChild()", "invalid argument #1 to 'FindFirstChild' (string expected, got nil)")]
    [InlineData("workspace:FindFirstChild(workspace)", "invalid argument #1 to 'FindFirstChild' (string expected, got Instance)")]
    [InlineData("workspace:FindFirstChild(5)", "invalid argument #1 to 'FindFirstChild' (string expected, got number)")]
    [InlineData("workspace:FindFirstChild('House', 'yes')", "invalid argument #2 to 'FindFirstChild' (boolean expected, got string)")]
    [InlineData("workspace:FindFirstChildOfClass()", "invalid argument #1 to 'FindFirstChildOfClass' (string expected, got nil)")]
    [InlineData("workspace:FindFirstAncestorWhichIsA({})", "invalid argument #1 to 'FindFirstAncestorWhichIsA' (string expected, got table)")]
    [InlineData("workspace:IsAncestorOf('House')", "invalid argument #1 to 'IsAncestorOf' (Instance expected, got string)")]
    [InlineData("workspace:IsDescendantOf(5)", "invalid argument #1 to 'IsDescendantOf' (Instance expected, got number)")]
    [InlineData("workspace:IsDescendantOf(game:GetService('RunService'))", "invalid argument #1 to 'IsDescendantOf' (Instance expected, got RunService)")]
    [InlineData("workspace:IsAncestorOf(game:GetService('RunService').Heartbeat)", "invalid argument #1 to 'IsAncestorOf' (Instance expected, got RBXScriptSignal)")]
    [InlineData("workspace.FindFirstChild('House')", "Expected ':' not '.' calling member function FindFirstChild")]
    [InlineData("workspace.GetChildren()", "Expected ':' not '.' calling member function GetChildren")]
    public void A_find_handed_the_wrong_thing_is_refused_in_Robloxs_words(string call, string error)
    {
        using ScriptLevel level = House.With($"local _ = {call}");

        level.Play().Tick();

        level.Errors.ShouldBe([error]);
    }

    [Fact]
    public void A_method_read_with_a_dot_is_a_function_that_takes_the_node_first()
    {
        string[] printed = House.Ask("""
            local find, children = workspace.FindFirstChild, workspace.GetChildren
            print(type(find), find(workspace, "House"), find(workspace.House, "Door"), #children(workspace.House))
            print(workspace.FindFirstChild == workspace.House.FindFirstChild)
            """);

        printed.ShouldBe(["function House Door 4", "true"]);
    }
}
