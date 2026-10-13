using System.Text.Json;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Entities;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// An entity class as a script class: read from its schema, with each input
/// a method and each output a signal, and written into the definitions file
/// and the JSON beside the classes the binding declares.
/// </summary>
public sealed class EntityClassApiTests
{
    private static readonly EntitySchema Door = new(
        "func_gate",
        displayName: "Gate",
        group: "Movers",
        placement: EntityPlacement.Brush,
        inputs: [new("Open", ParameterType.None), new("SetSpeed", ParameterType.Of(KeyvalueType.Float))],
        outputs: [new("OnOpen", ParameterType.None), new("OnMoved", ParameterType.Of(KeyvalueType.Vec3))]);

    private static ScriptApiDescription Api => LuauScriptApi.Description;

    [Fact]
    public void A_class_is_described_with_its_inputs_as_methods_and_its_outputs_as_signals()
    {
        ScriptClassDescription described = Api.WithEntityClasses([Door]).EntityClasses.ShouldHaveSingleItem();

        described.Name.ShouldBe("func_gate");
        described.Extends.ShouldBe("Part");
        described.Summary.ShouldBe("Gate. An entity class of the group Movers.");
        described.Properties.ShouldBeEmpty();
        described.Methods.Select(method => method.Name).ShouldBe(["Open", "SetSpeed"]);
        described.Methods[0].Signatures.ShouldHaveSingleItem().Parameters.ShouldBeEmpty();
        described.Methods[1].Signatures.ShouldHaveSingleItem().Parameters.ShouldBe([new ScriptParameter("value", "number")]);
        described.Methods[1].Signatures[0].Returns.ShouldBeEmpty();
        described.Signals.Select(signal => signal.Name).ShouldBe(["OnOpen", "OnMoved"]);
        described.Signals[0].Parameters.ShouldBeEmpty();
        described.Signals[1].Parameters.ShouldBe([new ScriptParameter("value", "Vector3")]);
    }

    [Fact]
    public void The_description_a_build_has_of_itself_knows_no_entity_class()
    {
        Api.EntityClasses.ShouldBeEmpty();
        Api.WithEntityClasses([Door]).Classes.ShouldBe(Api.Classes);
    }

    [Theory]
    [InlineData(EntityPlacement.Brush, "Part")]
    [InlineData(EntityPlacement.Volume, "Part")]
    [InlineData(EntityPlacement.Point, "Instance")]
    [InlineData(EntityPlacement.Abstract, "Instance")]
    public void A_class_placed_as_a_brush_extends_Part_and_any_other_extends_Instance(EntityPlacement placement, string extends)
    {
        var schema = new EntitySchema("thing", placement: placement);

        EntityClassApi.ExtendsOf(schema).ShouldBe(extends);
        Api.WithEntityClasses([schema]).EntityClasses.ShouldHaveSingleItem().Extends.ShouldBe(extends);
    }

    [Theory]
    [InlineData(KeyvalueType.Bool, "value: boolean")]
    [InlineData(KeyvalueType.Int, "value: number")]
    [InlineData(KeyvalueType.Float, "value: number")]
    [InlineData(KeyvalueType.String, "value: string")]
    [InlineData(KeyvalueType.TargetName, "name: string")]
    [InlineData(KeyvalueType.Vec2, "x: number, y: number")]
    [InlineData(KeyvalueType.Vec3, "value: Vector3")]
    [InlineData(KeyvalueType.Vec4, "x: number, y: number, z: number, w: number")]
    [InlineData(KeyvalueType.Color, "value: Vector3")]
    [InlineData(KeyvalueType.Angles, "value: Vector3")]
    [InlineData(KeyvalueType.NodeRef, "node: Instance?")]
    public void A_value_of_each_type_a_wire_carries_has_its_type_in_Luau(KeyvalueType type, string values)
    {
        Written(EntityClassApi.ValuesOf(ParameterType.Of(type))).ShouldBe(values);
    }

    [Fact]
    public void A_value_that_may_be_left_out_is_optional_and_an_input_or_output_may_take_none()
    {
        Written(EntityClassApi.ValuesOf(ParameterType.Optional(KeyvalueType.Float))).ShouldBe("value: number?");
        Written(EntityClassApi.ValuesOf(ParameterType.Optional(KeyvalueType.Vec2))).ShouldBe("x: number?, y: number?");
        Written(EntityClassApi.ValuesOf(ParameterType.Optional(KeyvalueType.NodeRef))).ShouldBe("node: Instance?");
        Written(EntityClassApi.ValuesOf(ParameterType.None)).ShouldBeEmpty();
        Written(EntityClassApi.SentValuesOf(ParameterType.None)).ShouldBeEmpty();
        Written(EntityClassApi.SentValuesOf(ParameterType.Optional(KeyvalueType.Float))).ShouldBe("value: number?");
        Written(EntityClassApi.SentValuesOf(ParameterType.Of(KeyvalueType.Vec3))).ShouldBe("value: Vector3");
    }

    // What the binding does with one: WireValues takes one value of five
    // kinds or none, and hands a listener the wire's text or nothing.
    [Fact]
    public void A_port_that_does_not_say_its_type_takes_one_value_a_wire_can_carry_and_sends_its_text()
    {
        Written(EntityClassApi.ValuesOf(ParameterType.NotSaid))
            .ShouldBe("value: (boolean | number | string | Vector3 | Instance)?");
        Written(EntityClassApi.SentValuesOf(ParameterType.NotSaid)).ShouldBe("text: string?");
    }

    [Fact]
    public void A_member_of_the_class_it_extends_wins_and_an_input_wins_over_an_output_of_its_name()
    {
        var schema = new EntitySchema(
            "clasher",
            placement: EntityPlacement.Brush,
            inputs: EntityPort.Named("GetFullName", "Toggle", "Name"),
            outputs: EntityPort.Named("Parent", "Toggle", "OnToggled", "IsA"));

        EntityClassMembers members = EntityClassApi.MembersOf(schema, Api);

        members.ClassName.ShouldBe("clasher");
        members.Extends.ShouldBe("Part");
        members.Inputs.Select(input => input.Name).ShouldBe(["Toggle"]);
        members.Outputs.Select(output => output.Name).ShouldBe(["OnToggled"]);
        members.TryGetInput("Toggle", out _).ShouldBeTrue();
        members.TryGetInput("toggle", out _).ShouldBeFalse();
        members.TryGetOutput("Toggle", out _).ShouldBeFalse();
        members.TryGetOutput("OnToggled", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("On-Open")]
    [InlineData("1st")]
    [InlineData("end")]
    [InlineData("Open Now")]
    [InlineData("")]
    public void A_name_Luau_cannot_write_is_no_member(string name)
    {
        var schema = new EntitySchema("odd", inputs: EntityPort.Named(name, "Open"), outputs: EntityPort.Named(name, "OnOpen"));

        EntityClassMembers members = EntityClassApi.MembersOf(schema, Api);

        EntityClassApi.IsLuauName(name).ShouldBeFalse();
        members.Inputs.Select(input => input.Name).ShouldBe(["Open"]);
        members.Outputs.Select(output => output.Name).ShouldBe(["OnOpen"]);
    }

    [Fact]
    public void Classes_come_in_the_order_of_their_names_and_one_Luau_cannot_declare_stays_out()
    {
        EntitySchema[] schemas =
        [
            new("trigger_once"), new("func_door"), new("Part"), new("my-class"), new("string"), new("func_door"),
            new("_ok"), new("while"),
        ];

        Api.WithEntityClasses(schemas).EntityClasses.Select(described => described.Name)
            .ShouldBe(["_ok", "func_door", "trigger_once"]);
    }

    [Fact]
    public void The_file_declares_a_class_after_the_class_it_extends_with_the_type_of_its_children()
    {
        var plain = new EntitySchema(
            "logic_poke",
            inputs: [new("Poke", ParameterType.NotSaid), new("Add", ParameterType.Optional(KeyvalueType.Float))],
            outputs: [new("OnPoked", ParameterType.NotSaid), new("OnNode", ParameterType.Of(KeyvalueType.NodeRef))]);

        string written = LuauDefinitions.Write(Api.WithEntityClasses([Door, plain]));

        written.ShouldContain("""

            --- Gate. An entity class of the group Movers.
            declare extern type func_gate extends Part with
                read OnOpen: RBXScriptSignal<()>
                read OnMoved: RBXScriptSignal<Vector3>
                function Open(self): ()
                function SetSpeed(self, value: number): ()
                [string]: Instance
            end

            --- logic_poke. An entity class.
            declare extern type logic_poke extends Instance with
                read OnPoked: RBXScriptSignal<string?>
                read OnNode: RBXScriptSignal<Instance?>
                function Poke(self, value: (boolean | number | string | Vector3 | Instance)?): ()
                function Add(self, value: number?): ()
                [string]: Instance
            end

            """.ReplaceLineEndings("\n"));
        written.IndexOf("declare extern type func_gate ", StringComparison.Ordinal)
            .ShouldBeGreaterThan(written.IndexOf("declare extern type Part ", StringComparison.Ordinal));
        written.IndexOf("declare extern type func_gate ", StringComparison.Ordinal)
            .ShouldBeLessThan(written.IndexOf("declare task: {", StringComparison.Ordinal));
    }

    [Fact]
    public void The_json_has_the_entity_classes_apart_from_the_classes_of_the_binding()
    {
        using JsonDocument json = JsonDocument.Parse(ScriptApiJson.Write(Api.WithEntityClasses([Door])));
        JsonElement root = json.RootElement;

        JsonElement gate = root.GetProperty("entityClasses").EnumerateArray().ShouldHaveSingleItem();
        gate.GetProperty("name").GetString().ShouldBe("func_gate");
        gate.GetProperty("extends").GetString().ShouldBe("Part");
        gate.GetProperty("methods").EnumerateArray().Select(method => method.GetProperty("name").GetString())
            .ShouldBe(["Open", "SetSpeed"]);
        gate.GetProperty("signals").EnumerateArray().Select(signal => signal.GetProperty("name").GetString())
            .ShouldBe(["OnOpen", "OnMoved"]);
        root.GetProperty("classes").EnumerateArray().Select(described => described.GetProperty("name").GetString())
            .ShouldNotContain("func_gate");
    }

    // Through .sentdef bytes, as the demo's switch and an editor read them.
    [Fact]
    public void Every_built_in_class_is_in_the_file_with_each_of_its_inputs_and_outputs()
    {
        IReadOnlyList<EntitySchema> schemas =
            EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(BuiltinEntities.Schemas)).Schemas;

        ScriptApiDescription api = Api.WithEntityClasses(schemas);
        string written = LuauDefinitions.Write(api);

        api.EntityClasses.Select(described => described.Name)
            .ShouldBe(BuiltinEntities.Schemas.Select(schema => schema.ClassName).Order(StringComparer.Ordinal));
        foreach (EntitySchema schema in schemas)
        {
            string body = BlockOf(written, $"declare extern type {schema.ClassName} extends {EntityClassApi.ExtendsOf(schema)} with");
            foreach (EntityPort input in schema.Inputs)
                body.ShouldContain($"\n    function {input.Name}(self", Case.Sensitive, schema.ClassName);
            foreach (EntityPort output in schema.Outputs)
                body.ShouldContain($"\n    read {output.Name}: RBXScriptSignal<", Case.Sensitive, schema.ClassName);
        }

        written.ShouldContain("\n    function Open(self): ()\n");
        written.ShouldContain("\n    function SetValue(self, value: number): ()\n");
        written.ShouldContain("\n    read OutValue: RBXScriptSignal<number>\n");
        written.ShouldContain("\n    read OnPressed: RBXScriptSignal<()>\n");
    }

    // A clash is seen the day it is made: the member would win, and the
    // input or output could not be reached from a script.
    [Fact]
    public void No_built_in_class_has_an_input_or_an_output_named_as_a_member_of_the_node_that_carries_it()
    {
        foreach (EntitySchema schema in BuiltinEntities.Schemas)
        {
            string carrier = EntityClassApi.ExtendsOf(schema);
            IReadOnlyCollection<string> taken = Api.MemberNamesOf(carrier);

            foreach (EntityPort port in schema.Inputs.Concat(schema.Outputs))
            {
                taken.ShouldNotContain(
                    port.Name,
                    $"{schema.ClassName} has an input or output named {port.Name}, which is a member of {carrier}. " +
                    "A script would get the member. Rename one of the two");
                EntityClassApi.IsLuauName(port.Name).ShouldBeTrue($"{schema.ClassName}.{port.Name} is no name Luau can write");
            }

            schema.InputNames.Intersect(schema.OutputNames).ShouldBeEmpty(
                $"{schema.ClassName} has an input and an output of one name, and a script would get the input");
            Api.FindClass(schema.ClassName).ShouldBeNull($"{schema.ClassName} is a class of the script API too");

            EntityClassMembers members = EntityClassApi.MembersOf(schema, Api);
            members.Inputs.ShouldBe(schema.Inputs);
            members.Outputs.ShouldBe(schema.Outputs);
        }
    }

    private static string Written(IReadOnlyList<ScriptParameter> values) =>
        string.Join(", ", values.Select(value => $"{value.Name}: {value.Type}"));

    // From the line that starts a declaration to the "end" that closes it.
    private static string BlockOf(string written, string start)
    {
        int at = written.IndexOf(start, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"the file has no '{start}'");
        return written[at..written.IndexOf("\nend\n", at, StringComparison.Ordinal)];
    }
}
