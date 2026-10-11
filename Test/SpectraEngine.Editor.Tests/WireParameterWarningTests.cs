using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The line the Sends section shows under a wire whose parameter its input
/// cannot take. It warns and changes nothing: the wire stays as typed.
/// </summary>
public sealed class WireParameterWarningTests
{
    private static readonly ParameterType Number = ParameterType.Of(KeyvalueType.Float);

    private static readonly EntitySchemaCatalog Catalog = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema(
            "math_counter",
            inputs:
            [
                new EntityPort("Add", ParameterType.Optional(KeyvalueType.Float)),
                new EntityPort("SetValue", Number),
                new EntityPort("GetValue", ParameterType.None),
            ],
            outputs: [new EntityPort("OutValue", Number), new EntityPort("OnHitMax", ParameterType.None)]),
        new EntitySchema(
            "logic_branch",
            inputs: [new EntityPort("SetValue", ParameterType.Of(KeyvalueType.Bool))],
            outputs: [new EntityPort("OnTrue", ParameterType.None)]),
        new EntitySchema(
            "func_door",
            inputs: [new EntityPort("Open", ParameterType.None)],
            outputs: [new EntityPort("OnOpen", ParameterType.None)]),

        // As a file from before the types reads: names and nothing more.
        new EntitySchema("old_thing", inputs: EntityPort.Named("Poke"), outputs: EntityPort.Named("OnPoked")),
    ]));

    private static readonly EntityTargetInfo[] Scene =
    [
        new("score", "math_counter"),
        new("branch", "logic_branch"),
        new("door", "func_door"),
        new("relic", "old_thing"),
        new("stranger", "from_another_game"),
        new("twin", "math_counter"),
        new("twin", "func_door"),
    ];

    [Fact]
    public void A_number_for_an_input_that_takes_one_says_nothing()
    {
        ConnectionRowModel row = Row("func_door", "OnOpen", "score", "SetValue", "5");

        row.HasParameterWarning.ShouldBeFalse();
        row.ParameterWarning.ShouldBe("");
    }

    [Fact]
    public void Text_for_an_input_that_takes_a_number_is_said_under_the_wire()
    {
        ConnectionRowModel row = Row("func_door", "OnOpen", "score", "SetValue", "five");

        row.HasParameterWarning.ShouldBeTrue();
        row.ParameterWarning.ShouldBe("SetValue takes a number, not 'five'");
    }

    [Fact]
    public void An_input_that_takes_a_flag_wants_one_or_zero()
    {
        Row("func_door", "OnOpen", "branch", "SetValue", "yes")
            .ParameterWarning.ShouldBe("SetValue takes 0 or 1, not 'yes'");
        Row("func_door", "OnOpen", "branch", "SetValue", "1").HasParameterWarning.ShouldBeFalse();
    }

    [Fact]
    public void A_parameter_for_an_input_that_takes_none_is_said()
    {
        Row("func_door", "OnOpen", "door", "Open", "now").ParameterWarning.ShouldBe("Open takes no parameter");
        Row("func_door", "OnOpen", "door", "Open", "").HasParameterWarning.ShouldBeFalse();
    }

    [Fact]
    public void An_input_that_needs_a_number_and_gets_none_is_said()
    {
        Row("func_door", "OnOpen", "score", "SetValue", "").ParameterWarning.ShouldBe("SetValue needs a number");
    }

    [Fact]
    public void An_input_that_also_works_without_its_number_says_nothing_with_none()
    {
        Row("func_door", "OnOpen", "score", "Add", "").HasParameterWarning.ShouldBeFalse();
        Row("func_door", "OnOpen", "score", "Add", "2").HasParameterWarning.ShouldBeFalse();
    }

    [Fact]
    public void A_parameter_on_a_wire_from_an_output_that_sends_its_own_value_is_not_used()
    {
        Row("math_counter", "OutValue", "branch", "SetValue", "1")
            .ParameterWarning.ShouldBe("OutValue sends its own value, so this parameter is not used");
    }

    [Fact]
    public void A_wire_from_an_output_that_sends_its_own_value_needs_no_parameter()
    {
        Row("math_counter", "OutValue", "score", "SetValue", "").HasParameterWarning.ShouldBeFalse();
    }

    [Fact]
    public void A_wire_to_self_arrives_at_the_class_it_leaves()
    {
        Row("func_door", "OnOpen", "!self", "Open", "now").ParameterWarning.ShouldBe("Open takes no parameter");
    }

    [Theory]
    [InlineData("relic", "Poke", "anything")]
    [InlineData("stranger", "Explode", "5")]
    [InlineData("twin", "SetValue", "five")]
    [InlineData("sco*", "SetValue", "five")]
    [InlineData("!activator", "SetValue", "five")]
    [InlineData("nobody", "SetValue", "five")]
    [InlineData("score", "SetVolume", "five")]
    public void Nothing_is_said_when_no_one_class_says_what_the_input_takes(
        string target, string input, string parameter)
    {
        Row("func_door", "OnOpen", target, input, parameter).HasParameterWarning.ShouldBeFalse();
    }

    [Theory]
    [InlineData("relic")]
    [InlineData("stranger")]
    [InlineData("twin")]
    [InlineData("sco*")]
    [InlineData("!activator")]
    [InlineData("nobody")]
    public void An_output_that_sends_its_own_value_is_said_whoever_the_wire_reaches(string target)
    {
        // Only the sender decides it, so the editor need not know the receiver.
        Row("math_counter", "OutValue", target, "SetValue", "5")
            .ParameterWarning.ShouldBe("OutValue sends its own value, so this parameter is not used");
    }

    [Fact]
    public void Nothing_is_said_about_an_input_while_the_list_of_entities_is_cut_short()
    {
        // A second entity named door may sit past the cut, and the wire
        // reaches that one too.
        var wire = new EntityConnection("OnOpen", "door", "Open", "now", 0f, EntityConnection.Infinite);
        Row("func_door", wire, truncated: false).ParameterWarning.ShouldBe("Open takes no parameter");

        ConnectionRowModel row = Row("func_door", wire, truncated: true);

        row.HasParameterWarning.ShouldBeFalse();
        row.InputChoices.ShouldContain("Open");
    }

    [Fact]
    public void What_does_not_hang_on_the_list_is_still_said_while_it_is_cut_short()
    {
        var toSelf = new EntityConnection("OnOpen", "!self", "Open", "now", 0f, EntityConnection.Infinite);
        var replaced = new EntityConnection("OutValue", "door", "Open", "5", 0f, EntityConnection.Infinite);

        Row("func_door", toSelf, truncated: true).ParameterWarning.ShouldBe("Open takes no parameter");
        Row("math_counter", replaced, truncated: true)
            .ParameterWarning.ShouldBe("OutValue sends its own value, so this parameter is not used");
    }

    [Fact]
    public void A_sender_of_a_class_nobody_knows_may_send_the_value_so_nothing_is_missing()
    {
        Row("from_another_game", "OnThing", "score", "SetValue", "").HasParameterWarning.ShouldBeFalse();

        // What is typed is still read.
        Row("from_another_game", "OnThing", "score", "SetValue", "five")
            .ParameterWarning.ShouldBe("SetValue takes a number, not 'five'");
    }

    [Fact]
    public void No_catalogue_means_no_warning()
    {
        var model = new EntityWiringModel((_, _) => { });
        model.Apply(Info("func_door", new EntityConnection("OnOpen", "score", "SetValue", "five", 0f, -1)));

        model.Rows[0].HasParameterWarning.ShouldBeFalse();
    }

    [Fact]
    public void A_level_that_loads_with_a_wrong_parameter_is_shown_and_not_changed()
    {
        var posts = new List<IReadOnlyList<EntityConnection>>();
        var model = new EntityWiringModel((_, wires) => posts.Add(wires)) { Schemas = Catalog };
        var wire = new EntityConnection("OnOpen", "score", "SetValue", "five", 0f, EntityConnection.Infinite);

        model.Apply(Info("func_door", wire));

        model.Rows[0].HasParameterWarning.ShouldBeTrue();
        model.Rows[0].ToConnection().ShouldBe(wire);
        posts.ShouldBeEmpty("showing a warning must not write to the level");
    }

    [Fact]
    public void A_wrong_parameter_is_kept_as_typed_and_not_refused()
    {
        var posts = new List<IReadOnlyList<EntityConnection>>();
        var model = new EntityWiringModel((_, wires) => posts.Add(wires)) { Schemas = Catalog };
        model.Apply(Info("func_door", new EntityConnection("OnOpen", "score", "SetValue", "5", 0f, -1)));
        ConnectionRowModel row = model.Rows[0];

        Type(row.ParameterField, "five");

        posts.ShouldHaveSingleItem()[0].Parameter.ShouldBe("five");
        row.ParameterField.HasRejection.ShouldBeFalse();
        row.ParameterWarning.ShouldBe("SetValue takes a number, not 'five'");
    }

    [Fact]
    public void The_warning_follows_each_edit_without_waiting_for_the_engine()
    {
        var model = new EntityWiringModel((_, _) => { }) { Schemas = Catalog };
        model.Apply(Info("func_door", new EntityConnection("OnOpen", "score", "SetValue", "five", 0f, -1)));
        ConnectionRowModel row = model.Rows[0];
        row.HasParameterWarning.ShouldBeTrue();

        Type(row.ParameterField, "5");
        row.HasParameterWarning.ShouldBeFalse();

        Type(row.TargetField, "door");
        Type(row.InputField, "Open");
        row.ParameterWarning.ShouldBe("Open takes no parameter");

        Type(row.ParameterField, "");
        row.HasParameterWarning.ShouldBeFalse();

        Type(row.TargetField, "branch");
        row.Input = "SetValue";
        row.ParameterWarning.ShouldBe("SetValue needs 0 or 1");
    }

    [Fact]
    public void Picking_another_output_judges_the_parameter_again()
    {
        var model = new EntityWiringModel((_, _) => { }) { Schemas = Catalog };
        model.Apply(Info("math_counter", new EntityConnection("OnHitMax", "branch", "SetValue", "1", 0f, -1)));
        ConnectionRowModel row = model.Rows[0];
        row.HasParameterWarning.ShouldBeFalse();

        row.Output = "OutValue";

        row.ParameterWarning.ShouldBe("OutValue sends its own value, so this parameter is not used");
    }

    [Fact]
    public void A_view_bound_to_the_warning_hears_it_come_and_go()
    {
        var model = new EntityWiringModel((_, _) => { }) { Schemas = Catalog };
        model.Apply(Info("func_door", new EntityConnection("OnOpen", "score", "SetValue", "5", 0f, -1)));
        ConnectionRowModel row = model.Rows[0];
        var heard = new List<string?>();
        row.PropertyChanged += (_, change) => heard.Add(change.PropertyName);

        Type(row.ParameterField, "five");

        heard.ShouldContain(nameof(ConnectionRowModel.ParameterWarning));
        heard.ShouldContain(nameof(ConnectionRowModel.HasParameterWarning));

        heard.Clear();
        Type(row.ParameterField, "6");

        heard.ShouldContain(nameof(ConnectionRowModel.HasParameterWarning));
    }

    [Fact]
    public void A_snapshot_that_changes_nothing_raises_nothing()
    {
        // Snapshots arrive about thirty times a second.
        var model = new EntityWiringModel((_, _) => { }) { Schemas = Catalog };
        EntityPanelInfo info = Info("func_door", new EntityConnection("OnOpen", "score", "SetValue", "five", 0f, -1));
        model.Apply(info);
        var heard = new List<string?>();
        model.Rows[0].PropertyChanged += (_, change) => heard.Add(change.PropertyName);

        model.Apply(info);

        heard.ShouldNotContain(nameof(ConnectionRowModel.ParameterWarning));
        heard.ShouldNotContain(nameof(ConnectionRowModel.HasParameterWarning));
    }

    [Theory]
    [InlineData(KeyvalueType.Bool, "0 or 1")]
    [InlineData(KeyvalueType.Int, "a whole number")]
    [InlineData(KeyvalueType.Float, "a number")]
    [InlineData(KeyvalueType.Vec2, "two numbers")]
    [InlineData(KeyvalueType.Vec3, "three numbers")]
    [InlineData(KeyvalueType.Color, "three numbers")]
    [InlineData(KeyvalueType.Angles, "three numbers")]
    [InlineData(KeyvalueType.Vec4, "four numbers")]
    [InlineData(KeyvalueType.NodeRef, "the id of a node")]
    public void Each_type_is_asked_for_in_the_words_a_person_would_type_it_in(KeyvalueType type, string wanted)
    {
        var wire = new EntityConnection("OnOpen", "thing", "Set", "x", 0f, EntityConnection.Infinite);

        WireParameterText.Warning(ParameterVerdict.Unreadable, in wire, ParameterType.Of(type))
            .ShouldBe($"Set takes {wanted}, not 'x'");
        WireParameterText.Warning(ParameterVerdict.Missing, in wire, ParameterType.Of(type))
            .ShouldBe($"Set needs {wanted}");
    }

    [Fact]
    public void A_wire_with_nothing_wrong_has_no_words()
    {
        var wire = new EntityConnection("OnOpen", "thing", "Set", "x", 0f, EntityConnection.Infinite);

        WireParameterText.Warning(ParameterVerdict.Fine, in wire, Number).ShouldBe("");
    }

    // One wire on a selected entity of senderClass, as a snapshot brings it.
    private static ConnectionRowModel Row(
        string senderClass, string output, string target, string input, string parameter) =>
        Row(
            senderClass,
            new EntityConnection(output, target, input, parameter, 0f, EntityConnection.Infinite),
            truncated: false);

    // truncated: the scene has more entities than the snapshot lists.
    private static ConnectionRowModel Row(string senderClass, EntityConnection wire, bool truncated)
    {
        var model = new EntityWiringModel((_, _) => { }) { Schemas = Catalog };
        model.Apply(Info(senderClass, wire, truncated));
        return model.Rows[0];
    }

    private static EntityPanelInfo Info(string senderClass, EntityConnection wire, bool truncated = false)
    {
        bool known = Catalog.TryGetSchema(senderClass, out EntitySchema? schema);
        return new EntityPanelInfo
        {
            NodeId = Guid.Parse("3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4"),
            ClassName = senderClass,
            IsKnown = known,
            Outputs = schema?.OutputNames ?? [],
            Connections = [new EntityConnectionInfo(wire, TargetResolves: true)],
            Targets = Scene,
            TargetsTruncated = truncated,
        };
    }

    // Types into a cell the way a person does: focus, text, Enter.
    private static void Type(PropertyFieldModel field, string text)
    {
        field.BeginEdit();
        field.Text = text;
        field.Commit();
    }
}
