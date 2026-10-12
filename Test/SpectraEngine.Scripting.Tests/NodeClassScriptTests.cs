using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a node is to a script: the classes IsA answers for, and that a node
/// of every class has the members of the classes its own extends.
/// </summary>
public sealed class NodeClassScriptTests
{
    // Every class of the script API a node can be, two entity classes and
    // one of Roblox's that nothing here is.
    private const string Classes = """
        local classes = {
            "Instance", "BasePart", "Part", "MeshPart", "Light", "PointLight", "SpotLight", "DirectionalLight",
            "SurfaceLight", "DiscLight", "Folder", "Script", "ModuleScript", "sayer", "func_door", "Model",
        }
        local function isA(node)
            local found = {}
            for _, class in classes do
                if node:IsA(class) then table.insert(found, class) end
            end
            return node.Name .. " is a " .. node.ClassName .. ": " .. table.concat(found, " ")
        end
        """;

    [Fact]
    public void A_node_is_of_its_class_and_of_every_class_that_one_extends()
    {
        string[] printed = House.Ask($$"""
            {{Classes}}
            local house = workspace.House
            for _, node in { workspace, workspace.Baseplate, workspace.Sun, house.Walls.North, house.Lamp,
                house.Lamp.Shade, house.Sign, workspace.Panel, workspace.Dish, workspace.Rules, script } do
                print(isA(node))
            end
            """);

        printed.ShouldBe(
        [
            "Root is a Folder: Instance Folder",
            "Baseplate is a Part: Instance BasePart Part",
            "Sun is a DirectionalLight: Instance Light DirectionalLight",
            "North is a Part: Instance BasePart Part",
            "Lamp is a PointLight: Instance Light PointLight",
            "Shade is a SpotLight: Instance Light SpotLight",
            "Sign is a MeshPart: Instance BasePart MeshPart",
            "Panel is a SurfaceLight: Instance Light SurfaceLight",
            "Dish is a DiscLight: Instance Light DiscLight",
            "Rules is a ModuleScript: Instance ModuleScript",
            "Main is a Script: Instance Script",
        ]);
    }

    [Fact]
    public void A_brush_entity_is_its_entity_class_and_a_part_and_a_point_entity_is_only_its_class()
    {
        string[] printed = House.Ask($$"""
            {{Classes}}
            print(isA(workspace.House.Door))
            print(isA(workspace.Relay))
            """);

        printed.ShouldBe(
        [
            "Door is a sayer: Instance BasePart Part sayer",
            "Relay is a sayer: Instance sayer",
        ]);
    }

    [Fact]
    public void An_entity_on_a_light_a_mesh_or_a_script_is_that_kind_too()
    {
        using var level = new ScriptLevel();
        House.Build(level);
        foreach (string name in new[] { "Lamp", "Sign" })
            level.Scene.Root.Traverse().Single(node => node.Name == name).Entity = new(SayingEntity.Class);

        level.AddScript(level.Scene.Root, $$"""
            {{Classes}}
            print(isA(workspace.House.Lamp))
            print(isA(workspace.House.Sign))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "Lamp is a sayer: Instance Light PointLight sayer",
            "Sign is a sayer: Instance BasePart MeshPart sayer",
        ]);
    }

    [Fact]
    public void IsA_takes_a_class_name_as_it_is_written_and_nothing_else()
    {
        string[] printed = House.Ask("""
            local north = workspace.House.Walls.North
            print(north:IsA("part"), north:IsA("Part "), north:IsA(""), north:IsA("North"))
            print(pcall(function() return north:IsA() end))
            print(pcall(function() return north:IsA(north) end))
            """);

        printed[0].ShouldBe("false false false false");
        printed[1].ShouldEndWith("invalid argument #1 to 'IsA' (string expected, got nil)");
        printed[2].ShouldEndWith("invalid argument #1 to 'IsA' (string expected, got Instance)");
    }

    // The class of a node is asked first, and it hands a name it does not
    // declare to the class it extends. So a read, a write and a call each
    // reach Instance through the node's own class.
    [Fact]
    public void A_node_of_every_class_has_the_members_of_Instance()
    {
        string[] printed = House.Ask("""
            local house = workspace.House
            local nodes = { house.Walls, house.Walls.North, house.Door, house.Lamp, house.Lamp.Shade, house.Sign,
                workspace.Sun, workspace.Panel, workspace.Dish, workspace.Relay, workspace.Rules, script }

            for _, node in nodes do
                print(node.ClassName, node:GetFullName(), type(node.GetFullName), node:IsA(node.ClassName))
            end

            for _, node in nodes do
                local class = node.ClassName
                node.Name = class .. "!"
                local _, readOnly = pcall(function() node.ClassName = "Part" end)
                local _, missing = pcall(function() return node.Anchored end)
                print(node.Name, string.match(readOnly, "Unable.*$"), string.match(missing, "Anchored.*$"))
            end
            """);

        (string Class, string Path)[] nodes =
        [
            ("Folder", "Root.House.Walls"), ("Part", "Root.House.Walls.North"), ("sayer", "Root.House.Door"),
            ("PointLight", "Root.House.Lamp"), ("SpotLight", "Root.House.Lamp.Shade"), ("MeshPart", "Root.House.Sign"),
            ("DirectionalLight", "Root.Sun"), ("SurfaceLight", "Root.Panel"), ("DiscLight", "Root.Dish"),
            ("sayer", "Root.Relay"), ("ModuleScript", "Root.Rules"), ("Script", "Root.Main"),
        ];

        printed.ShouldBe(
        [
            .. nodes.Select(node => $"{node.Class} {node.Path} function true"),
            .. nodes.Select(node =>
                $"{node.Class}! Unable to assign property ClassName. Property is read only " +
                $"Anchored is not a valid member of {node.Class} \"{node.Class}!\""),
        ]);
    }

    [Fact]
    public void The_definitions_file_has_each_class_after_the_one_it_extends_and_a_child_by_its_name()
    {
        string file = LuauDefinitions.Write(LuauScriptApi.Description);

        file.ShouldContain("declare extern type BasePart extends Instance with\nend\n");
        file.ShouldContain("declare extern type Part extends BasePart with\nend\n");
        file.ShouldContain("declare extern type SpotLight extends Light with\nend\n");
        file.ShouldContain("    [string]: Instance\nend\n");

        Place(file, "Instance with").ShouldBeLessThan(Place(file, "BasePart extends"));
        Place(file, "BasePart extends").ShouldBeLessThan(Place(file, "MeshPart extends"));
        Place(file, "Light extends Instance").ShouldBeLessThan(Place(file, "DirectionalLight extends"));
    }

    // Core says what a class extends for IsA, and the script API says it
    // again for the definitions file. A script would be told two things.
    [Fact]
    public void The_classes_of_the_script_API_extend_what_Core_says_they_extend()
    {
        ScriptApiDescription api = LuauScriptApi.Description;

        foreach (NodeClassKind kind in Enum.GetValues<NodeClassKind>())
        {
            for (string? name = NodeClass.NameOf(kind); name is not null; name = NodeClass.BaseOf(name))
            {
                ScriptClassDescription described = api.FindClass(name).ShouldNotBeNull($"the script API declares no {name}");

                described.Extends.ShouldBe(NodeClass.BaseOf(name) ?? "", $"what {name} extends");
            }
        }
    }

    private static int Place(string file, string declared)
    {
        int at = file.IndexOf("declare extern type " + declared, StringComparison.Ordinal);
        at.ShouldBeGreaterThan(0, declared);
        return at;
    }
}
