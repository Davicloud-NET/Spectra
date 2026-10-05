using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

/// <summary>The Sends section: the shell's half of entity wiring.</summary>
// Every edit posts the whole connection list, since the command is absolute.
public sealed class EntityWiringPanelTests
{
    private static readonly Guid NodeId = Guid.Parse("3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4");

    private static EntityConnection Wire(
        string output = "OnOpen", string target = "light1", string input = "TurnOn",
        string param = "", float delay = 0f, int times = EntityConnection.Infinite) =>
        new(output, target, input, param, delay, times);

    private static EntityPanelInfo Info(
        bool known = true,
        IReadOnlyList<string>? outputs = null,
        params EntityConnectionInfo[] wires) => new()
        {
            NodeId = NodeId,
            ClassName = "func_door",
            IsKnown = known,
            Outputs = outputs ?? ["OnOpen", "OnClose"],
            Connections = wires,
        };

    private static EntityConnectionInfo Resolved(EntityConnection wire) => new(wire, true);

    private static EntityConnectionInfo Unresolved(EntityConnection wire) => new(wire, false);

    private sealed class Rig
    {
        public List<(Guid NodeId, EntityConnection[] Wires)> Posts { get; } = [];

        public PropertyPanelModel Panel { get; }

        public Rig() => Panel = new PropertyPanelModel(
            _ => { }, _ => { }, _ => { },
            (id, wires) => Posts.Add((id, [.. wires])));

        public EntityWiringModel Wiring => Panel.Wiring;

        public void Publish(EntityPanelInfo? info) => Panel.Apply([], info is null ? 0 : 1, info);

        public EntityConnection[] LastPost => Posts[^1].Wires;
    }

    [Fact]
    public void The_section_appears_only_for_an_entity()
    {
        var rig = new Rig();
        rig.Wiring.HasEntity.ShouldBeFalse();

        rig.Publish(Info(wires: Resolved(Wire())));
        rig.Wiring.HasEntity.ShouldBeTrue();
        rig.Wiring.Rows.Count.ShouldBe(1);

        rig.Publish(null);
        rig.Wiring.HasEntity.ShouldBeFalse();
        rig.Wiring.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void The_rows_keep_the_authored_order()
    {
        var rig = new Rig();
        rig.Publish(Info(
            wires:
            [
                Resolved(Wire("OnClose", "z")),
                Resolved(Wire("OnOpen", "a")),
                Resolved(Wire("OnClose", "m")),
            ]));

        rig.Wiring.Rows.Select(r => r.TargetField.Text).ShouldBe(["z", "a", "m"]);
    }

    [Fact]
    public void An_entity_with_no_wires_says_so()
    {
        var rig = new Rig();
        rig.Publish(Info());

        rig.Wiring.IsEmpty.ShouldBeTrue();

        rig.Publish(Info(wires: Resolved(Wire())));
        rig.Wiring.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Every_field_of_a_wire_is_shown()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire("OnClose", "light2", "TurnOff", "3", 1.5f, 2))));

        ConnectionRowModel row = rig.Wiring.Rows[0];
        row.Output.ShouldBe("OnClose");
        row.TargetField.Text.ShouldBe("light2");
        row.InputField.Text.ShouldBe("TurnOff");
        row.ParameterField.Text.ShouldBe("3");
        row.DelayField.Text.ShouldBe("1.5");
        row.TimesField.Text.ShouldBe("2");
    }

    [Fact]
    public void An_unresolved_target_warns_and_the_wire_stays()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Unresolved(Wire(target: "nobody"))));

        ConnectionRowModel row = rig.Wiring.Rows[0];
        row.HasTargetWarning.ShouldBeTrue();
        row.TargetWarning.ShouldBe("Nothing here is named 'nobody'");
        row.TargetField.Text.ShouldBe("nobody", "warn and keep, never warn and drop");
    }

    [Fact]
    public void An_empty_target_says_it_is_empty_rather_than_naming_nothing()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Unresolved(Wire(target: ""))));

        rig.Wiring.Rows[0].TargetWarning.ShouldBe("No target set");
    }

    [Fact]
    public void A_resolved_target_shows_no_warning()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire())));

        rig.Wiring.Rows[0].HasTargetWarning.ShouldBeFalse();
        rig.Wiring.Rows[0].TargetWarning.ShouldBe("");
    }

    [Fact]
    public void The_dropdown_offers_what_the_class_declares()
    {
        var rig = new Rig();
        rig.Publish(Info(outputs: ["OnOpen", "OnClose"], wires: Resolved(Wire("OnOpen", "a"))));

        rig.Wiring.Rows[0].OutputChoices.ShouldBe(["OnOpen", "OnClose"]);
        rig.Wiring.Rows[0].HasOutputChoices.ShouldBeTrue();
    }

    [Fact]
    public void An_authored_output_nothing_declares_is_typed_rather_than_picked()
    {
        // A dropdown that cannot show the stored value renders blank and
        // writes the blank back on the next edit, so the row is a text box.
        var rig = new Rig();
        rig.Publish(Info(outputs: ["OnOpen"], wires: Resolved(Wire("OnSomethingElse", "a"))));

        ConnectionRowModel row = rig.Wiring.Rows[0];
        row.HasOutputChoices.ShouldBeFalse();
        row.Output.ShouldBe("OnSomethingElse");
        row.OutputField.Text.ShouldBe("OnSomethingElse");
        row.OutputChoices.ShouldBe(["OnOpen"], "the menu still offers only what the class declares");
    }

    [Fact]
    public void Typing_a_declared_output_flips_the_row_back_to_the_dropdown()
    {
        var rig = new Rig();
        rig.Publish(Info(outputs: ["OnOpen"], wires: Resolved(Wire("OnSomethingElse", "a"))));

        ConnectionRowModel row = rig.Wiring.Rows[0];
        row.HasOutputChoices.ShouldBeFalse();

        row.OutputField.BeginEdit();
        row.OutputField.Text = "OnOpen";
        row.OutputField.Commit();

        row.HasOutputChoices.ShouldBeTrue();
        rig.LastPost[0].Output.ShouldBe("OnOpen");
    }

    [Fact]
    public void An_unchanged_publish_hands_the_dropdown_the_same_list_instance()
    {
        // A fresh list per publish makes the ComboBox drop its selection. One
        // instance, as the engine publishes: the list comes off the schema.
        string[] declared = ["OnOpen", "OnClose"];

        var rig = new Rig();
        rig.Publish(Info(outputs: declared, wires: Resolved(Wire("OnOpen", "a"))));

        rig.Wiring.Rows[0].OutputChoices.ShouldBeSameAs(declared);

        rig.Publish(Info(outputs: declared, wires: Resolved(Wire("OnClose", "b"))));

        rig.Wiring.Rows[0].OutputChoices.ShouldBeSameAs(declared);
    }

    [Fact]
    public void A_dropdown_clearing_its_own_selection_posts_nothing()
    {
        // A ComboBox clears SelectedItem when its ItemsSource is replaced, and
        // a two-way binding delivers that like a click.
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire("OnOpen", "a"))));

        rig.Wiring.Rows[0].Output = string.Empty;

        rig.Posts.ShouldBeEmpty();
        rig.Wiring.Rows[0].Output.ShouldBe("OnOpen");
    }

    [Fact]
    public void The_typed_output_commits_rather_than_writing_per_keystroke()
    {
        var rig = new Rig();
        rig.Publish(Info(known: false, outputs: [], wires: Resolved(Wire("", "a"))));

        PropertyFieldModel output = rig.Wiring.Rows[0].OutputField;
        output.BeginEdit();
        output.Text = "OnFoo";
        rig.Posts.ShouldBeEmpty();

        output.Commit();
        rig.LastPost[0].Output.ShouldBe("OnFoo");
        rig.Wiring.Rows[0].Output.ShouldBe("OnFoo");
    }

    [Fact]
    public void Typing_an_output_never_changes_the_dropdowns_item_source()
    {
        // Replacing the item source makes the control discard its selection,
        // and a binding will not re-push a value it already pushed.
        var rig = new Rig();
        rig.Publish(Info(known: false, outputs: [], wires: Resolved(Wire("", "a"))));

        IReadOnlyList<string> source = rig.Wiring.Rows[0].OutputChoices;

        PropertyFieldModel output = rig.Wiring.Rows[0].OutputField;
        output.BeginEdit();
        output.Text = "OnHandmade";
        output.Commit();

        rig.Publish(Info(known: false, outputs: [], wires: Resolved(Wire("OnHandmade", "a"))));

        rig.Wiring.Rows[0].OutputChoices.ShouldBeSameAs(source);
        rig.Wiring.Rows[0].HasOutputChoices.ShouldBeFalse("a class with no schema stays a text box");
        rig.Wiring.Rows[0].OutputField.Text.ShouldBe("OnHandmade");
    }

    [Fact]
    public void A_class_with_no_schema_gets_a_typed_field_instead_of_an_empty_dropdown()
    {
        var rig = new Rig();
        rig.Publish(Info(known: false, outputs: [], wires: Resolved(Wire("OnWhatever", "a"))));

        rig.Wiring.ShowsUnknownOutputs.ShouldBeTrue();
        rig.Wiring.Rows[0].HasOutputChoices.ShouldBeFalse();
        rig.Wiring.Rows[0].OutputField.Text.ShouldBe("OnWhatever");
    }

    [Fact]
    public void A_field_commit_posts_the_whole_list_with_the_edit_in_place()
    {
        var rig = new Rig();
        rig.Publish(Info(
            wires: [Resolved(Wire("OnOpen", "a")), Resolved(Wire("OnClose", "b"))]));

        PropertyFieldModel target = rig.Wiring.Rows[1].TargetField;
        target.BeginEdit();
        target.Text = "c";
        target.Commit();

        rig.Posts.Count.ShouldBe(1);
        rig.Posts[0].NodeId.ShouldBe(NodeId);
        rig.LastPost.Length.ShouldBe(2);
        rig.LastPost[0].TargetName.ShouldBe("a", "the untouched wire went along unchanged");
        rig.LastPost[1].TargetName.ShouldBe("c");
        rig.LastPost[1].Output.ShouldBe("OnClose");
    }

    [Fact]
    public void An_empty_parameter_is_a_value_and_commits()
    {
        // Every other field reverts an empty commit.
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire(param: "3"))));

        PropertyFieldModel parameter = rig.Wiring.Rows[0].ParameterField;
        parameter.BeginEdit();
        parameter.Text = "";
        parameter.Commit();

        rig.LastPost[0].Parameter.ShouldBe("");
    }

    [Fact]
    public void An_unparseable_delay_reverts_rather_than_sticking()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire(delay: 2f))));

        PropertyFieldModel delay = rig.Wiring.Rows[0].DelayField;
        delay.BeginEdit();
        delay.Text = "soon";
        delay.Commit();

        rig.Posts.ShouldBeEmpty();
        delay.Text.ShouldBe("2");

        delay.BeginEdit();
        delay.Text = "-1";
        delay.Commit();

        rig.Posts.ShouldBeEmpty();
        delay.Text.ShouldBe("2");
    }

    [Fact]
    public void The_infinite_sentinel_is_shown_as_a_word()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire(times: EntityConnection.Infinite))));

        rig.Wiring.Rows[0].TimesField.Text.ShouldBe(ConnectionRowModel.ForeverLabel);
    }

    [Fact]
    public void Typing_the_word_posts_the_sentinel_in_any_case()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire(times: 4))));

        PropertyFieldModel times = rig.Wiring.Rows[0].TimesField;
        times.BeginEdit();
        times.Text = "forever";
        times.Commit();

        rig.LastPost[0].TimesToFire.ShouldBe(EntityConnection.Infinite);
    }

    [Fact]
    public void A_word_that_is_not_the_label_is_refused_with_the_format()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire(times: 4))));

        PropertyFieldModel times = rig.Wiring.Rows[0].TimesField;
        times.BeginEdit();
        times.Text = "lots";
        times.Commit();

        times.HasRejection.ShouldBeTrue();
        times.Rejection.ShouldContain(ConnectionRowModel.ForeverLabel);
        times.Text.ShouldBe("4");
    }

    [Fact]
    public void A_negative_delay_is_refused_and_says_why()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire(delay: 1.5f))));

        PropertyFieldModel delay = rig.Wiring.Rows[0].DelayField;
        delay.BeginEdit();
        delay.Text = "-2";
        delay.Commit();

        delay.HasRejection.ShouldBeTrue();
        delay.Rejection.ShouldContain("0 or more");
        delay.Text.ShouldBe("1.5");
    }

    [Fact]
    public void Any_negative_times_normalises_to_the_infinite_sentinel()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire(times: 4))));

        PropertyFieldModel times = rig.Wiring.Rows[0].TimesField;
        times.BeginEdit();
        times.Text = "-7";
        times.Commit();

        rig.LastPost[0].TimesToFire.ShouldBe(EntityConnection.Infinite);
    }

    [Fact]
    public void Picking_an_output_posts_it_and_a_refresh_does_not()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire("OnOpen", "a"))));
        rig.Posts.ShouldBeEmpty();

        rig.Wiring.Rows[0].Output = "OnClose";
        rig.LastPost[0].Output.ShouldBe("OnClose");

        int posts = rig.Posts.Count;
        rig.Publish(Info(wires: Resolved(Wire("OnClose", "a"))));
        rig.Posts.Count.ShouldBe(posts, "a refresh is not a click");
    }

    [Fact]
    public void Add_and_remove_post_the_new_list()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire("OnOpen", "a"))));

        rig.Wiring.Add();
        rig.Wiring.Rows.Count.ShouldBe(2);
        rig.LastPost.Length.ShouldBe(2);
        rig.LastPost[1].Output.ShouldBe("OnOpen", "a new wire starts on the first declared output");
        rig.LastPost[1].TargetName.ShouldBe("");
        rig.LastPost[1].TimesToFire.ShouldBe(EntityConnection.Infinite);

        rig.Wiring.Remove(rig.Wiring.Rows[0]);
        rig.LastPost.Length.ShouldBe(1);
        rig.LastPost[0].TargetName.ShouldBe("");
    }

    [Fact]
    public void Add_does_nothing_without_an_entity()
    {
        var rig = new Rig();
        rig.Wiring.Add();

        rig.Posts.ShouldBeEmpty();
        rig.Wiring.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void A_focused_field_stops_taking_refreshes()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire(target: "a"))));

        PropertyFieldModel target = rig.Wiring.Rows[0].TargetField;
        target.BeginEdit();
        target.Text = "half typed";

        rig.Publish(Info(wires: Resolved(Wire(target: "a"))));

        target.Text.ShouldBe("half typed");
    }

    [Fact]
    public void A_stale_snapshot_does_not_undo_an_add()
    {
        // The engine echoes an edit a publish or two later.
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire("OnOpen", "a"))));

        rig.Wiring.Add();
        rig.Wiring.Rows.Count.ShouldBe(2);

        rig.Publish(Info(wires: Resolved(Wire("OnOpen", "a"))));
        rig.Wiring.Rows.Count.ShouldBe(2, "the snapshot describes a frame from before the add");

        rig.Publish(Info(
            wires:
            [
                Resolved(Wire("OnOpen", "a")),
                Unresolved(new EntityConnection("OnOpen", "", "", "", 0f, EntityConnection.Infinite)),
            ]));

        rig.Wiring.Rows.Count.ShouldBe(2, "and now it agrees");
    }

    [Fact]
    public void The_engine_wins_once_the_hold_expires()
    {
        // The engine can refuse an edit, e.g. during play mode.
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire("OnOpen", "a"))));

        rig.Wiring.Add();
        rig.Wiring.Rows.Count.ShouldBe(2);

        for (int i = 0; i < EntityWiringModel.HoldSnapshots; i++)
            rig.Publish(Info(wires: Resolved(Wire("OnOpen", "a"))));

        rig.Wiring.Rows.Count.ShouldBe(1, "the engine refused, visibly");
    }

    [Fact]
    public void Selecting_a_different_entity_drops_a_pending_edit()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire("OnOpen", "a"))));
        rig.Wiring.Add();

        var other = new EntityPanelInfo
        {
            NodeId = Guid.NewGuid(),
            ClassName = "logic_relay",
            IsKnown = true,
            Outputs = ["OnTrigger"],
            Connections = [],
        };

        rig.Publish(other);

        rig.Wiring.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void An_unchanged_publish_keeps_the_controls_it_already_built()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: Resolved(Wire(target: "a"))));

        ConnectionRowModel before = rig.Wiring.Rows[0];

        rig.Publish(Info(wires: Resolved(Wire(target: "b"))));

        rig.Wiring.Rows[0].ShouldBeSameAs(before);
        before.TargetField.Text.ShouldBe("b");
    }

    [Fact]
    public void A_wire_removed_by_the_engine_is_taken_off_the_panel()
    {
        var rig = new Rig();
        rig.Publish(Info(wires: [Resolved(Wire("OnOpen", "a")), Resolved(Wire("OnClose", "b"))]));
        rig.Wiring.Rows.Count.ShouldBe(2);

        rig.Publish(Info(wires: Resolved(Wire("OnOpen", "a"))));

        rig.Wiring.Rows.Count.ShouldBe(1);
        rig.Wiring.Rows[0].TargetField.Text.ShouldBe("a");
    }

    [Fact]
    public void A_panel_built_without_a_wiring_callback_still_works()
    {
        var panel = new PropertyPanelModel(_ => { }, _ => { }, _ => { });

        panel.Apply([], 1, Info(wires: Resolved(Wire())));
        Should.NotThrow(() => panel.Wiring.Add());
    }
}

/// <summary>The target picker and the input dropdown behind it.</summary>
// Inputs are offered only when the target resolves to one entity of a known class.
public sealed class WiringTargetPickerTests
{
    private static EntitySchemaCatalog Catalog(params EntitySchema[] schemas) =>
        EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(schemas));

    private static EntitySchema Relay(params string[] inputs) =>
        new("logic_relay", inputs: inputs, outputs: ["OnTrigger"]);

    private static readonly EntityTargetInfo[] Scene =
    [
        new("relay", "logic_relay"),
        new("timer", "logic_timer"),
    ];

    [Fact]
    public void A_target_resolving_to_one_known_class_offers_its_inputs()
    {
        EntitySchemaCatalog catalog = Catalog(Relay("Trigger", "Enable", "Disable"));

        IReadOnlyList<string> inputs = ConnectionRowModel.InputsFor("relay", Scene, catalog);

        inputs.Count.ShouldBe(3);
        inputs.ShouldContain("Trigger");
    }

    [Fact]
    public void The_list_is_the_schemas_own_instance()
    {
        EntitySchemaCatalog catalog = Catalog(Relay("Trigger"));

        IReadOnlyList<string> first = ConnectionRowModel.InputsFor("relay", Scene, catalog);
        IReadOnlyList<string> again = ConnectionRowModel.InputsFor("relay", Scene, catalog);

        // Replacing a bound item source makes the control drop its selection.
        ReferenceEquals(first, again).ShouldBeTrue();
    }

    [Fact]
    public void Two_entities_sharing_a_name_offer_nothing()
    {
        EntitySchemaCatalog catalog = Catalog(Relay("Trigger"));

        EntityTargetInfo[] twins =
        [
            new("door", "logic_relay"),
            new("door", "logic_timer"),
        ];

        // Legal: the wire fires at both.
        ConnectionRowModel.InputsFor("door", twins, catalog).Count.ShouldBe(0);
    }

    [Fact]
    public void A_wildcard_offers_nothing()
    {
        EntitySchemaCatalog catalog = Catalog(Relay("Trigger"));

        ConnectionRowModel.InputsFor("rel*", Scene, catalog).Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("!self")]
    [InlineData("!activator")]
    [InlineData("!caller")]
    public void A_runtime_token_offers_nothing(string token)
    {
        EntitySchemaCatalog catalog = Catalog(Relay("Trigger"));

        ConnectionRowModel.InputsFor(token, Scene, catalog).Count.ShouldBe(0);
    }

    [Fact]
    public void A_class_this_build_has_no_schema_for_offers_nothing()
    {
        EntitySchemaCatalog catalog = Catalog(Relay("Trigger"));

        EntityTargetInfo[] unknown = [new("thing", "from_another_game")];

        ConnectionRowModel.InputsFor("thing", unknown, catalog).Count.ShouldBe(0);
    }

    [Fact]
    public void A_target_naming_nothing_offers_nothing()
    {
        EntitySchemaCatalog catalog = Catalog(Relay("Trigger"));

        ConnectionRowModel.InputsFor("nobody", Scene, catalog).Count.ShouldBe(0);
        ConnectionRowModel.InputsFor("", Scene, catalog).Count.ShouldBe(0);
        ConnectionRowModel.InputsFor("relay", Scene, null).Count.ShouldBe(0);
    }

    [Fact]
    public void The_picker_offers_the_runtime_tokens_before_the_scene()
    {
        var model = new EntityWiringModel((_, _) => { })
        {
            Schemas = Catalog(Relay("Trigger")),
        };

        model.Apply(new EntityPanelInfo
        {
            NodeId = Guid.NewGuid(),
            ClassName = "logic_timer",
            IsKnown = true,
            Outputs = ["OnTimer"],
            Connections =
            [
                new EntityConnectionInfo(
                    new EntityConnection("OnTimer", "relay", "Trigger", "", 0f, EntityConnection.Infinite),
                    TargetResolves: true),
            ],
            Targets = Scene,
        });

        ConnectionRowModel row = model.Rows[0];

        row.TargetChoices[0].ShouldBe("!self");
        row.TargetChoices[1].ShouldBe("!activator");
        row.TargetChoices[2].ShouldBe("!caller");
        row.TargetChoices.ShouldContain("relay");
        row.TargetChoices.ShouldContain("timer");

        row.HasInputChoices.ShouldBeTrue();
        row.InputChoices.ShouldContain("Trigger");
    }

    [Fact]
    public void Retyping_the_target_changes_which_inputs_are_offered()
    {
        var model = new EntityWiringModel((_, _) => { })
        {
            Schemas = Catalog(Relay("Trigger")),
        };

        model.Apply(new EntityPanelInfo
        {
            NodeId = Guid.NewGuid(),
            ClassName = "logic_timer",
            IsKnown = true,
            Outputs = ["OnTimer"],
            Connections =
            [
                new EntityConnectionInfo(
                    new EntityConnection("OnTimer", "relay", "Trigger", "", 0f, EntityConnection.Infinite),
                    TargetResolves: true),
            ],
            Targets = Scene,
        });

        ConnectionRowModel row = model.Rows[0];
        row.HasInputChoices.ShouldBeTrue();

        // No publish in between: the inputs must follow the commit itself.
        row.TargetField.BeginEdit();
        row.TargetField.Text = "rel*";
        row.TargetField.Commit();

        row.InputChoices.Count.ShouldBe(0);
        row.HasInputChoices.ShouldBeFalse();
    }

    [Fact]
    public void Picking_a_target_writes_it_through_the_fields_own_commit()
    {
        List<IReadOnlyList<EntityConnection>> posted = [];

        var model = new EntityWiringModel((_, wires) => posted.Add(wires))
        {
            Schemas = Catalog(Relay("Trigger")),
        };

        model.Apply(new EntityPanelInfo
        {
            NodeId = Guid.NewGuid(),
            ClassName = "logic_timer",
            IsKnown = true,
            Outputs = ["OnTimer"],
            Connections =
            [
                new EntityConnectionInfo(
                    new EntityConnection("OnTimer", "", "", "", 0f, EntityConnection.Infinite),
                    TargetResolves: false),
            ],
            Targets = Scene,
        });

        model.Rows[0].PickTarget("relay");

        posted.Count.ShouldBe(1);
        posted[0][0].TargetName.ShouldBe("relay");
    }
}
