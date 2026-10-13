using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Entities;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What of an entity class no script reaches is said: by whoever writes the
/// definitions file, and to the scripts of a level the first time one asks a
/// node of that class. Which members win goes by the node that carries it.
/// </summary>
public sealed class EntityClassGapsTests
{
    private static readonly EntitySchema Odd = new(
        "odd",
        placement: EntityPlacement.Brush,
        inputs: EntityPort.Named("On.Press", "Name", "Shut"),
        outputs: EntityPort.Named("Parent", "Shut", "OnShut", "1st"));

    private static ScriptApiDescription Api => LuauScriptApi.Description;

    [Fact]
    public void The_built_in_classes_have_no_gap()
    {
        EntityClassGaps.Of(BuiltinEntities.Schemas, Api).ShouldBeEmpty();
    }

    // A class is placed as a brush or as a point, and its node may still
    // carry anything: a point class a light, a brush class a mesh. So a name
    // is held free on every class of nodes, not only where the placement says.
    [Fact]
    public void No_built_in_class_has_an_input_or_an_output_that_a_member_of_any_class_of_nodes_takes()
    {
        string[] carriers = [.. Api.Classes.Where(IsOfNodes).Select(described => described.Name)];
        carriers.ShouldContain("Instance");
        carriers.ShouldContain("Part");
        carriers.ShouldContain("PointLight");
        carriers.ShouldNotContain("RunService");

        foreach (EntitySchema schema in BuiltinEntities.Schemas)
        {
            foreach (string carrier in carriers)
            {
                EntityClassGaps.OfPorts(schema, Api, carrier).ShouldBeEmpty(
                    $"on a {carrier}, a script would get the node's own member. Rename one of the two");
            }
        }
    }

    [Fact]
    public void A_class_the_file_cannot_declare_is_named_with_why_and_once()
    {
        EntitySchema[] schemas = [new("my-door"), new("func_fine"), new("string"), new("Part"), new("my-door")];

        EntityClassGaps.Of(schemas, Api).ShouldBe(
        [
            "The entity class my-door is not in the definitions file: Luau cannot write its name as a type.",
            "The entity class string is not in the definitions file: Luau has a type of that name.",
            "The entity class Part is not in the definitions file: the script API has a class of that name.",
        ]);
    }

    [Fact]
    public void An_input_and_an_output_no_script_reaches_are_named_with_why()
    {
        EntityClassGaps.Of([Odd], Api).ShouldBe(
        [
            "No script reaches the input On.Press of odd: Luau cannot write the name after a dot or a colon.",
            "No script reaches the input Name of odd: Part has a member of that name, which wins.",
            "No script reaches the output Parent of odd: Part has a member of that name, which wins.",
            "No script reaches the output Shut of odd: the input of that name wins.",
            "No script reaches the output 1st of odd: Luau cannot write the name after a dot or a colon.",
        ]);
    }

    // With classes of its own: the build's node classes all have the members of Instance today.
    [Fact]
    public void Which_members_win_goes_by_the_class_of_the_node_that_carries_the_entity()
    {
        var api = new ScriptApiDescription
        {
            Classes =
            [
                Class("Instance", "", "Name"),
                Class("Part", "Instance", "CanCollide"),
                Class("PointLight", "Instance", "Enabled"),
            ],
        };
        var gate = new EntitySchema(
            "gate", placement: EntityPlacement.Brush, inputs: EntityPort.Named("CanCollide", "Enabled", "Open", "Name"));

        Inputs(EntityClassApi.MembersOf(gate, api)).ShouldBe(["Enabled", "Open"]);
        Inputs(EntityClassApi.MembersOf(gate, api, "Part")).ShouldBe(["Enabled", "Open"]);
        Inputs(EntityClassApi.MembersOf(gate, api, "Instance")).ShouldBe(["CanCollide", "Enabled", "Open"]);
        Inputs(EntityClassApi.MembersOf(gate, api, "PointLight")).ShouldBe(["CanCollide", "Open"]);
        EntityClassApi.MembersOf(gate, api, "PointLight").Extends.ShouldBe("PointLight");
        EntityClassGaps.OfPorts(gate, api, "PointLight").ShouldBe(
        [
            "No script reaches the input Enabled of gate: PointLight has a member of that name, which wins.",
            "No script reaches the input Name of gate: PointLight has a member of that name, which wins.",
        ]);
    }

    [Fact]
    public void The_scripts_of_a_level_are_told_once_what_they_cannot_reach_on_a_node_of_a_class()
    {
        using var level = new ScriptLevel();
        level.Part("Box", Vector3.Zero).Entity = new EntityData(EchoEntity.ClashingClass);
        level.Part("Chest", Vector3.Zero).Entity = new EntityData(EchoEntity.ClashingClass);
        level.AddScript(level.Scene.Root, """
            workspace.Box:Shut()
            workspace.Chest:Shut()
            print(workspace.Box.OnShut)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        ScriptMessage[] warned = [.. level.Sink.Messages.Where(message => message.Severity == ScriptSeverity.Warning)];
        warned.Select(message => message.Text).ShouldBe(
        [
            "No script reaches the input GetFullName of clasher: Part has a member of that name, which wins.",
            "No script reaches the input Name of clasher: Part has a member of that name, which wins.",
            "No script reaches the output Parent of clasher: Part has a member of that name, which wins.",
            "No script reaches the output IsA of clasher: Part has a member of that name, which wins.",
            "No script reaches the output Shut of clasher: the input of that name wins.",
        ]);
        warned.ShouldAllBe(message => message.Script.Length == 0);
    }

    [Fact]
    public void A_class_with_no_gap_is_asked_with_no_warning()
    {
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Echo").Entity = new EntityData(EchoEntity.Class);
        level.AddScript(level.Scene.Root, "workspace.Echo:Ping()");

        level.Play().Tick();

        level.Sink.Messages.ShouldAllBe(message => message.Severity == ScriptSeverity.Print);
    }

    private static string[] Inputs(EntityClassMembers members) => [.. members.Inputs.Select(input => input.Name)];

    // Instance, and every class that extends it however far up.
    private static bool IsOfNodes(ScriptClassDescription described)
    {
        ScriptClassDescription? at = described;
        for (int depth = 0; at is not null && depth < 32; depth++)
        {
            if (at.Name == NodeClass.Instance)
                return true;

            at = at.Extends.Length == 0 ? null : Api.FindClass(at.Extends);
        }

        return false;
    }

    private static ScriptClassDescription Class(string name, string extends, string property) => new()
    {
        Name = name,
        Extends = extends,
        Properties = [new ScriptPropertyDescription { Name = property, Type = "any" }],
    };
}
