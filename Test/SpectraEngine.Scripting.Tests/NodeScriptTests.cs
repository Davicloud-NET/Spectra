using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a script sees of the level's nodes: its own node, the root, and the
/// name and class of each.
/// </summary>
public sealed class NodeScriptTests
{
    [Fact]
    public void A_script_reaches_its_node_its_parent_and_the_root()
    {
        using var level = new ScriptLevel();
        SceneNode door = level.Part("Door", new Vector3(1f, 2f, 3f));
        level.AddScript(door, """
            print(script, script.Name, script.Parent, script.Parent.Parent)
            print(script.Parent.Parent == workspace, workspace == game.Workspace, workspace.Parent)
            print(workspace == game:GetService("Workspace"), script.Parent == script.Parent, script == script.Parent)
            print(typeof(script), typeof(workspace), tostring(script.Parent))
            """, "Opener");

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "Opener Opener Door Root",
            "true true nil",
            "true true false",
            "Instance Instance Door",
        ]);
    }

    [Fact]
    public void A_node_held_as_a_table_key_is_found_again_by_another_read_of_it()
    {
        string[] printed = ScriptLevel.Run("""
            local seen = { [script.Parent] = 'the part' }
            print(seen[script.Parent], seen[workspace])
            """);

        printed.ShouldBe(["the part nil"]);
    }

    [Fact]
    public void A_nodes_class_name_is_its_entity_class_or_its_kind_in_Robloxs_words()
    {
        using var level = new ScriptLevel();
        SceneNode root = level.Scene.Root;
        level.Part("Part", Vector3.Zero);
        level.Part("Sayer", Vector3.Zero).Entity = new EntityData(SayingEntity.Class);
        root.CreateChild("Folder");
        root.CreateChild("Lamp").Light = new Light { Kind = LightKind.Point };
        root.CreateChild("Spot").Light = new Light { Kind = LightKind.Spot };
        root.CreateChild("Module").Script = new Script("Scripts/module.luau") { IsModule = true };
        level.AddScript(root, "print(script.ClassName, script.Parent.ClassName)");

        foreach (string name in new[] { "Part", "Sayer", "Folder", "Lamp", "Spot", "Module", "Sun", "Baseplate" })
        {
            SceneNode node = root.Children.Single(child => child.Name == name);
            level.AddScript(node, "print(script.Parent.Name, script.Parent.ClassName)");
        }

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.Order().ShouldBe(
        [
            "Baseplate Part",
            "Folder Folder",
            "Lamp PointLight",
            "Module ModuleScript",
            "Part Part",
            "Sayer sayer",
            "Script Folder",
            "Spot SpotLight",
            "Sun DirectionalLight",
        ]);
    }
}
