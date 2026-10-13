using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a script finds on a node of an entity class, against what the
/// description of that class says, and who wins a name: a member of the
/// node's own class, then an input or an output, then a child.
/// </summary>
public sealed class EntityClassBindingTests
{
    private static ScriptApiDescription Api => LuauScriptApi.Description;

    [Theory]
    [InlineData(EchoEntity.Class)]
    [InlineData(EchoEntity.ClashingClass)]
    public void A_node_of_a_class_has_the_methods_and_signals_its_description_gives_it_and_no_other(string className)
    {
        EntitySchema schema = className == EchoEntity.Class ? EchoEntity.Schema : EchoEntity.ClashingSchema;
        ScriptClassDescription described = Api.WithEntityClasses([schema]).EntityClasses.ShouldHaveSingleItem();
        IReadOnlyCollection<string> ofTheCarrier = Api.MemberNamesOf(described.Extends);

        // Every name of the schema, and each again in another case and with a letter more.
        string[] ports = [.. schema.InputNames.Concat(schema.OutputNames).Distinct()];
        string[] names = [.. ports, .. ports.Select(name => name.ToLowerInvariant()), .. ports.Select(name => name + "s")];

        using var level = new ScriptLevel();
        SceneNode holder = schema.Placement == EntityPlacement.Brush
            ? level.Part("Holder", Vector3.Zero)
            : level.Scene.Root.CreateChild("Holder");
        holder.Entity = new EntityData(className);
        level.AddScript(level.Scene.Root, $$"""
            local holder = workspace.Holder
            local found = {}
            for _, name in { {{string.Join(", ", names.Select(name => $"\"{name}\""))}} } do
                local isThere, value = pcall(function() return holder[name] end)
                if isThere and type(value) == "function" then
                    table.insert(found, name .. "()")
                elseif isThere and string.sub(tostring(value), 1, 7) == "Signal " then
                    table.insert(found, name .. "~")
                elseif isThere then
                    table.insert(found, name)
                end
            end
            print(table.concat(found, " "))
            """, "Asker");

        level.Play().Tick();

        // A name of the carrier's own class is that member, whatever the schema says.
        IEnumerable<string> expected = names
            .Where(name => !ofTheCarrier.Contains(name))
            .Select(name =>
                described.Methods.Any(method => method.Name == name) ? name + "()"
                : described.Signals.Any(signal => signal.Name == name) ? name + "~"
                : "")
            .Where(name => name.Length > 0);

        level.Errors.ShouldBeEmpty();
        string[] found = level.Printed.ShouldHaveSingleItem().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        found.Where(name => !ofTheCarrier.Contains(name.TrimEnd('(', ')', '~'))).ShouldBe(expected);
    }

    [Fact]
    public void A_member_of_the_node_wins_over_an_input_and_an_output_of_its_name()
    {
        using var level = new ScriptLevel();
        Box(level);
        level.AddScript(level.Scene.Root, """
            local box = workspace.Box
            print(box:GetFullName(), box.Name, box.Parent == workspace, box:IsA("clasher"))
            box.Name = "Crate"
            print(box.Name)
            """);

        level.Play().Tick();

        // Nothing was sent: each was the node's own member.
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Root.Box Box true true", "Crate"]);
    }

    [Fact]
    public void An_input_and_an_output_win_over_a_child_of_their_name_and_the_child_is_still_found_by_asking()
    {
        using var level = new ScriptLevel();
        SceneNode box = Box(level);
        box.CreateChild("Lid");
        box.CreateChild("Hatch");
        box.CreateChild("Plain");
        level.AddScript(level.Scene.Root, """
            local box = workspace.Box
            print(type(box.Lid), box.Hatch, box.Plain)
            print(box:FindFirstChild("Lid"), box:FindFirstChild("Hatch").ClassName)
            box:Lid()
            print(select(2, pcall(function() box:Plain() end)))
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            "function Signal Hatch Plain",
            "Lid Folder",
            $"{here}:5: attempt to call a Instance value",
            "Box got Lid ''",
        ]);
    }

    [Fact]
    public void An_input_wins_over_an_output_of_its_name()
    {
        using var level = new ScriptLevel();
        Box(level);
        level.AddScript(level.Scene.Root, """
            local box = workspace.Box
            print(type(box.Shut), box.OnShut)
            box:Shut()
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["function Signal OnShut", "Box got Shut ''"]);
    }

    private static SceneNode Box(ScriptLevel level)
    {
        SceneNode box = level.Part("Box", Vector3.Zero);
        box.Entity = new EntityData(EchoEntity.ClashingClass);
        return box;
    }
}
