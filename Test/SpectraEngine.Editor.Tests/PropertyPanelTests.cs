using SpectraEngine.Core.Inspection;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The property panel's commit policy: when a typed value reaches the scene,
/// and when the scene is allowed to overwrite what somebody is typing.
/// </summary>
public sealed class PropertyPanelTests
{
    private sealed class Rig
    {
        public List<PropertyEdit> Edits { get; } = [];
        public List<string> GesturesOpened { get; } = [];
        public List<bool> GesturesClosed { get; } = [];
        public PropertyPanelModel Panel { get; }

        public Rig() => Panel = new PropertyPanelModel(
            Edits.Add, GesturesOpened.Add, GesturesClosed.Add);

        public void Publish(params PropertyRow[] rows) => Panel.Apply(rows, 1);
        public void Publish(int selection, params PropertyRow[] rows) => Panel.Apply(rows, selection);

        public PropertyRowModel Row(PropertyId id) =>
            Panel.Groups.SelectMany(g => g.Rows).Single(r => r.Id == id);
    }

    private static PropertyRow Vector(PropertyId id, Vector3 value, PropertyAxes mixed = PropertyAxes.None) =>
        new()
        {
            Group = "Transform", Name = id.ToString(), Id = id, Kind = PropertyKind.Vector3,
            Vector = value, Choices = [], PresentCount = 1, SelectionCount = 1, MixedAxes = mixed,
        };

    private static PropertyRow Text(PropertyId id, string value) =>
        new()
        {
            Group = "Node", Name = "Name", Id = id, Kind = PropertyKind.Text,
            Text = value, Choices = [], PresentCount = 1, SelectionCount = 1,
        };

    private static PropertyRow Flag(PropertyId id, bool value) =>
        new()
        {
            Group = "Light", Name = "Enabled", Id = id, Kind = PropertyKind.Boolean,
            Flag = value, Choices = [], PresentCount = 1, SelectionCount = 1,
        };

    private static PropertyRow Choice(PropertyId id, string value, params string[] options) =>
        new()
        {
            Group = "Brush", Name = "Kind", Id = id, Kind = PropertyKind.Choice,
            Text = value, Choices = options, PresentCount = 1, SelectionCount = 1,
        };

    [Fact]
    public void A_refresh_does_not_touch_a_field_that_is_being_edited()
    {
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, new Vector3(1f, 2f, 3f)));

        PropertyFieldModel x = rig.Row(PropertyId.Position).Fields[0];
        x.BeginEdit();
        x.Text = "12";

        rig.Publish(Vector(PropertyId.Position, new Vector3(99f, 2f, 3f)));

        x.Text.ShouldBe("12", "the field belongs to the person typing in it");
    }

    [Fact]
    public void A_field_that_is_not_being_edited_follows_the_scene()
    {
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, new Vector3(1f, 2f, 3f)));

        rig.Publish(Vector(PropertyId.Position, new Vector3(7f, 2f, 3f)));

        rig.Row(PropertyId.Position).Fields[0].Text.ShouldBe("7");
    }

    [Fact]
    public void A_committed_field_starts_following_the_scene_again()
    {
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, new Vector3(1f, 2f, 3f)));

        PropertyFieldModel x = rig.Row(PropertyId.Position).Fields[0];
        x.BeginEdit();
        x.Text = "12";
        x.Commit();

        rig.Publish(Vector(PropertyId.Position, new Vector3(50f, 2f, 3f)));

        x.Text.ShouldBe("50");
    }

    [Fact]
    public void Committing_a_vector_cell_writes_only_that_axis()
    {
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, new Vector3(1f, 2f, 3f)));

        PropertyFieldModel y = rig.Row(PropertyId.Position).Fields[1];
        y.BeginEdit();
        y.Text = "40";
        y.Commit();

        rig.Edits.Count.ShouldBe(1);
        rig.Edits[0].Id.ShouldBe(PropertyId.Position);
        rig.Edits[0].Axes.ShouldBe(PropertyAxes.Y);
        rig.Edits[0].Vector.Y.ShouldBe(40f);
    }

    [Fact]
    public void Committing_an_unchanged_field_applies_nothing()
    {
        // Tabbing through fields must not reach the undo stack.
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, new Vector3(1f, 2f, 3f)));

        PropertyFieldModel x = rig.Row(PropertyId.Position).Fields[0];
        x.BeginEdit();
        x.Commit();

        rig.Edits.ShouldBeEmpty();
    }

    [Fact]
    public void A_field_that_was_never_focused_cannot_commit()
    {
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, new Vector3(1f, 2f, 3f)));

        rig.Row(PropertyId.Position).Fields[0].Commit();

        rig.Edits.ShouldBeEmpty();
    }

    [Fact]
    public void Text_that_will_not_parse_reverts_rather_than_sticking()
    {
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, new Vector3(1f, 2f, 3f)));

        PropertyFieldModel x = rig.Row(PropertyId.Position).Fields[0];
        x.BeginEdit();
        x.Text = "not a number";
        x.Commit();

        rig.Edits.ShouldBeEmpty();
        x.Text.ShouldBe("1");
    }

    [Fact]
    public void Escape_puts_the_live_value_back_and_applies_nothing()
    {
        // Blur commits, so Escape is the only way to abandon a typed value.
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, new Vector3(1f, 2f, 3f)));

        PropertyFieldModel z = rig.Row(PropertyId.Position).Fields[2];
        z.BeginEdit();
        z.Text = "999";
        z.Revert();

        rig.Edits.ShouldBeEmpty();
        z.Text.ShouldBe("3");
    }

    [Fact]
    public void A_name_is_applied_as_text_rather_than_parsed()
    {
        var rig = new Rig();
        rig.Publish(Text(PropertyId.NodeName, "Wall"));

        // The name is edited in the panel header.
        PropertyFieldModel field = rig.Panel.NameField;
        field.BeginEdit();
        field.Text = "Doorway";
        field.Commit();

        rig.Edits.Single().Text.ShouldBe("Doorway");
    }

    [Fact]
    public void A_mixed_cell_shows_nothing_rather_than_one_nodes_value()
    {
        var rig = new Rig();
        rig.Publish(2, Vector(PropertyId.Position, new Vector3(1f, 2f, 3f), PropertyAxes.Y));

        IReadOnlyList<PropertyFieldModel> fields = rig.Row(PropertyId.Position).Fields;

        fields[1].Text.ShouldBeEmpty();
        fields[1].IsMixed.ShouldBeTrue();
        fields[1].Placeholder.ShouldBe("mixed");

        fields[0].Text.ShouldBe("1", "the settled axes still show their value");
        fields[2].Text.ShouldBe("3");
    }

    [Fact]
    public void Leaving_a_mixed_cell_empty_applies_nothing()
    {
        var rig = new Rig();
        rig.Publish(2, Vector(PropertyId.Position, new Vector3(1f, 2f, 3f), PropertyAxes.Y));

        PropertyFieldModel y = rig.Row(PropertyId.Position).Fields[1];
        y.BeginEdit();
        y.Commit();

        rig.Edits.ShouldBeEmpty();
    }

    [Fact]
    public void Typing_into_a_mixed_cell_applies_to_the_whole_selection()
    {
        var rig = new Rig();
        rig.Publish(2, Vector(PropertyId.Position, new Vector3(1f, 2f, 3f), PropertyAxes.Y));

        PropertyFieldModel y = rig.Row(PropertyId.Position).Fields[1];
        y.BeginEdit();
        y.Text = "0";
        y.Commit();

        rig.Edits.Single().Axes.ShouldBe(PropertyAxes.Y);
        rig.Edits.Single().Vector.Y.ShouldBe(0f);
    }

    [Fact]
    public void A_checkbox_applies_on_the_click_because_there_is_nothing_to_finish()
    {
        var rig = new Rig();
        rig.Publish(Flag(PropertyId.LightEnabled, true));

        rig.Row(PropertyId.LightEnabled).Flag = false;

        rig.Edits.Single().Id.ShouldBe(PropertyId.LightEnabled);
        rig.Edits.Single().Flag.ShouldBeFalse();
    }

    [Fact]
    public void A_refresh_of_a_checkbox_does_not_apply_itself_back_to_the_scene()
    {
        // Assigning the refreshed value looks like a click unless it is guarded.
        var rig = new Rig();
        rig.Publish(Flag(PropertyId.LightEnabled, true));
        rig.Edits.Clear();

        rig.Publish(Flag(PropertyId.LightEnabled, false));

        rig.Edits.ShouldBeEmpty();
        rig.Row(PropertyId.LightEnabled).Flag.ShouldBeFalse();
    }

    [Fact]
    public void A_choice_applies_on_selection_and_not_on_refresh()
    {
        var rig = new Rig();
        rig.Publish(Choice(PropertyId.BrushKind, "World", "World", "Part"));
        rig.Edits.Clear();

        rig.Publish(Choice(PropertyId.BrushKind, "Part", "World", "Part"));
        rig.Edits.ShouldBeEmpty();

        rig.Row(PropertyId.BrushKind).Choice = "World";
        rig.Edits.Single().Text.ShouldBe("World");
    }

    [Fact]
    public void Rows_are_patched_rather_than_replaced_between_refreshes()
    {
        // A fresh collection resets scroll, drops focus and loses a half-typed value.
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, Vector3.Zero));
        PropertyRowModel before = rig.Row(PropertyId.Position);

        rig.Publish(Vector(PropertyId.Position, Vector3.One));

        rig.Row(PropertyId.Position).ShouldBeSameAs(before);
    }

    [Fact]
    public void A_change_in_which_properties_exist_rebuilds_the_rows()
    {
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, Vector3.Zero));

        rig.Publish(Vector(PropertyId.Position, Vector3.Zero), Flag(PropertyId.LightEnabled, true));

        rig.Panel.Groups.SelectMany(g => g.Rows).Select(r => r.Id)
            .ShouldBe([PropertyId.Position, PropertyId.LightEnabled]);
    }

    [Fact]
    public void Sections_come_from_runs_of_equal_groups()
    {
        var rig = new Rig();
        rig.Publish(
            Text(PropertyId.NodeName, "Wall"),
            Vector(PropertyId.Position, Vector3.Zero),
            Vector(PropertyId.Scale, Vector3.One),
            Choice(PropertyId.BrushKind, "World", "World", "Part"));

        // The name goes to the header, so there is no "Node" section.
        rig.Panel.Groups.Select(g => g.Name).ShouldBe(["Transform", "Brush"]);
        rig.Panel.Groups[0].Rows.Count.ShouldBe(2);
        rig.Panel.NameField.Text.ShouldBe("Wall");
    }

    [Fact]
    public void The_name_becomes_the_header_and_the_id_is_not_shown()
    {
        var rig = new Rig();
        rig.Publish(
            Text(PropertyId.NodeName, "WallNorth"),
            new PropertyRow
            {
                Group = "Node", Name = "Id", Id = PropertyId.NodeId,
                Kind = PropertyKind.ReadOnlyText, Text = "1ef302b1-1aa4-42ed-8aa1-7490bbbb0000",
                Choices = [], PresentCount = 1, SelectionCount = 1,
            },
            Vector(PropertyId.Position, Vector3.Zero));

        rig.Panel.NameField.Text.ShouldBe("WallNorth");
        rig.Panel.Groups.SelectMany(g => g.Rows).Select(r => r.Id).ShouldBe([PropertyId.Position]);
    }

    [Fact]
    public void Rows_still_refresh_when_a_published_row_is_not_rendered()
    {
        // The panel skips two published rows, so its rows and the snapshot's
        // do not line up by index.
        var rig = new Rig();
        rig.Publish(
            Text(PropertyId.NodeName, "Wall"),
            new PropertyRow
            {
                Group = "Node", Name = "Id", Id = PropertyId.NodeId,
                Kind = PropertyKind.ReadOnlyText, Text = "id", Choices = [],
                PresentCount = 1, SelectionCount = 1,
            },
            Vector(PropertyId.Position, new Vector3(1f, 2f, 3f)),
            Vector(PropertyId.Scale, new Vector3(4f, 5f, 6f)));

        rig.Row(PropertyId.Position).Fields[1].Text.ShouldBe("2");
        rig.Row(PropertyId.Scale).Fields[2].Text.ShouldBe("6");
    }

    [Fact]
    public void A_kind_is_derived_from_the_sections_the_selection_grew()
    {
        var rig = new Rig();
        rig.Publish(
            Text(PropertyId.NodeName, "DoorwayCut"),
            Choice(PropertyId.BrushKind, "World", "World", "Part"),
            Choice(PropertyId.BrushOperation, "Subtractive", "Additive", "Subtractive"));

        // Subtractive outranks the kind, as it does in the tree.
        rig.Panel.HeaderKind.ShouldBe("Cut");
    }

    [Fact]
    public void A_colour_is_shown_in_sRGB_and_read_back_as_linear()
    {
        var rig = new Rig();
        rig.Publish(new PropertyRow
        {
            Group = "Light", Name = "Color", Id = PropertyId.LightColor, Kind = PropertyKind.Color,
            Vector = Vector3.One, Choices = [], PresentCount = 1, SelectionCount = 1,
        });

        PropertyRowModel row = rig.Row(PropertyId.LightColor);
        row.Hex.ShouldBe("#FFFFFF");
        row.Fields[0].Text.ShouldBe("#FFFFFF");

        PropertyFieldModel cell = row.Fields[0];
        cell.BeginEdit();
        cell.Text = "#808080";
        cell.Commit();

        // sRGB mid grey is about 0.216 linear.
        rig.Edits.Count.ShouldBe(1);
        rig.Edits[0].Id.ShouldBe(PropertyId.LightColor);
        rig.Edits[0].Vector.X.ShouldBeInRange(0.20f, 0.23f);
    }

    [Fact]
    public void An_unreadable_colour_puts_the_last_good_one_back()
    {
        var rig = new Rig();
        rig.Publish(new PropertyRow
        {
            Group = "Light", Name = "Color", Id = PropertyId.LightColor, Kind = PropertyKind.Color,
            Vector = Vector3.One, Choices = [], PresentCount = 1, SelectionCount = 1,
        });

        PropertyRowModel row = rig.Row(PropertyId.LightColor);
        PropertyFieldModel cell = row.Fields[0];

        // A half-typed hex must not be parsed, or the box reverts on every keystroke.
        cell.BeginEdit();
        cell.Text = "#80";
        rig.Publish(new PropertyRow
        {
            Group = "Light", Name = "Color", Id = PropertyId.LightColor, Kind = PropertyKind.Color,
            Vector = Vector3.One, Choices = [], PresentCount = 1, SelectionCount = 1,
        });

        rig.Edits.ShouldBeEmpty();
        cell.Text.ShouldBe("#80");

        cell.Commit();
        rig.Edits.ShouldBeEmpty();
        cell.Text.ShouldBe("#FFFFFF");
    }

    [Fact]
    public void A_committed_field_takes_the_next_refresh()
    {
        // Commit must end the edit even while the box keeps focus, or the field
        // shows the typed number after the object has moved.
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, Vector3.Zero));

        PropertyFieldModel field = rig.Row(PropertyId.Position).Fields[0];
        field.BeginEdit();
        field.Text = "5";
        field.Commit();

        field.IsEditing.ShouldBeFalse();

        rig.Publish(Vector(PropertyId.Position, new Vector3(9f, 0f, 0f)));
        field.Text.ShouldBe("9");
    }

    [Fact]
    public void A_drag_on_one_axis_leaves_a_sibling_being_typed_into_alone()
    {
        // A drag clears the scrub guard on every cell of the row, so the typing
        // guard has to be separate. Pointer capture does not move keyboard focus.
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, Vector3.Zero));

        PropertyRowModel row = rig.Row(PropertyId.Position);
        PropertyFieldModel x = row.Fields[0];
        PropertyFieldModel y = row.Fields[1];

        x.BeginEdit();
        x.Text = "12.5";

        foreach (PropertyFieldModel cell in row.Fields)
            cell.BeginScrub();
        row.ScrubTo(y, 3f);
        foreach (PropertyFieldModel cell in row.Fields)
            cell.EndScrub();

        rig.Publish(Vector(PropertyId.Position, new Vector3(0f, 3f, 0f)));

        x.Text.ShouldBe("12.5");
        x.IsEditing.ShouldBeTrue();
    }

    [Fact]
    public void A_scrub_is_one_gesture_and_the_field_ignores_refreshes_inside_it()
    {
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, Vector3.Zero));

        PropertyRowModel row = rig.Row(PropertyId.Position);
        PropertyFieldModel field = row.Fields[1];

        rig.Panel.BeginGesture(row.Name);
        foreach (PropertyFieldModel cell in row.Fields)
            cell.BeginScrub();

        row.ScrubTo(field, 1.5f);
        row.ScrubTo(field, 2.5f);

        // A stale publish mid-drag.
        rig.Publish(Vector(PropertyId.Position, Vector3.Zero));
        field.Text.ShouldBe("2.5");

        foreach (PropertyFieldModel cell in row.Fields)
            cell.EndScrub();
        rig.Panel.EndGesture(commit: true);

        rig.GesturesOpened.ShouldBe(["Position"]);
        rig.GesturesClosed.ShouldBe([true]);
        rig.Edits.Count.ShouldBe(2);
        rig.Edits.ShouldAllBe(e => e.Axes == PropertyAxes.Y);

        rig.Publish(Vector(PropertyId.Position, new Vector3(0f, 7f, 0f)));
        field.Text.ShouldBe("7");
    }

    [Fact]
    public void An_empty_selection_reports_that_there_is_nothing_to_show()
    {
        var rig = new Rig();
        rig.Publish(0);

        rig.Panel.HasSelection.ShouldBeFalse();
        rig.Panel.Groups.ShouldBeEmpty();
    }

    [Fact]
    public void The_partial_label_says_how_far_a_bulk_edit_will_reach()
    {
        var rig = new Rig();
        rig.Publish(5, new PropertyRow
        {
            Group = "Brush", Name = "Kind", Id = PropertyId.BrushKind, Kind = PropertyKind.Choice,
            Text = "World", Choices = ["World", "Part"], PresentCount = 3, SelectionCount = 5,
        });

        PropertyRowModel row = rig.Row(PropertyId.BrushKind);
        row.IsPartial.ShouldBeTrue();
        row.PartialLabel.ShouldBe("3 of 5");
    }

    private static PropertyRow Number(PropertyId id, string name, float value) =>
        new()
        {
            Group = "Light", Name = name, Id = id, Kind = PropertyKind.Number,
            Number = value, Choices = [], PresentCount = 1, SelectionCount = 1,
        };

    [Fact]
    public void An_unparseable_number_reverts_and_says_what_was_expected()
    {
        var rig = new Rig();
        rig.Publish(Number(PropertyId.LightIntensity, "Intensity", 40f));

        PropertyFieldModel cell = rig.Row(PropertyId.LightIntensity).Fields[0];
        cell.BeginEdit();
        cell.Text = "abc";
        cell.Commit();

        Assert.Empty(rig.Edits);
        Assert.Equal("40", cell.Text);
        Assert.True(cell.HasRejection);
        Assert.Contains("a number", cell.Rejection);
    }

    [Fact]
    public void A_value_the_editor_would_refuse_is_refused_before_it_is_posted()
    {
        // Light.Range throws on zero, so the panel has to refuse it first.
        var rig = new Rig();
        rig.Publish(Number(PropertyId.LightRange, "Range", 8f));

        PropertyFieldModel cell = rig.Row(PropertyId.LightRange).Fields[0];
        cell.BeginEdit();
        cell.Text = "0";
        cell.Commit();

        Assert.Empty(rig.Edits);
        Assert.Contains("greater than 0", cell.Rejection);
        Assert.Contains("Range", cell.Rejection);
    }

    [Fact]
    public void An_intensity_of_zero_is_accepted_because_the_editor_accepts_it()
    {
        var rig = new Rig();
        rig.Publish(Number(PropertyId.LightIntensity, "Intensity", 40f));

        PropertyFieldModel cell = rig.Row(PropertyId.LightIntensity).Fields[0];
        cell.BeginEdit();
        cell.Text = "0";
        cell.Commit();

        Assert.Single(rig.Edits);
        Assert.False(cell.HasRejection);
    }

    [Fact]
    public void A_bad_hex_says_what_a_colour_looks_like()
    {
        var rig = new Rig();
        rig.Publish(new PropertyRow
        {
            Group = "Light", Name = "Color", Id = PropertyId.LightColor, Kind = PropertyKind.Color,
            Vector = new Vector3(1f, 1f, 1f), Choices = [], PresentCount = 1, SelectionCount = 1,
        });

        PropertyFieldModel cell = rig.Row(PropertyId.LightColor).Fields[0];
        cell.BeginEdit();
        cell.Text = "#80";
        cell.Commit();

        Assert.Empty(rig.Edits);
        Assert.Contains("#RRGGBB", cell.Rejection);
    }

    [Fact]
    public void A_rejection_clears_on_the_next_keystroke()
    {
        var rig = new Rig();
        rig.Publish(Number(PropertyId.LightIntensity, "Intensity", 40f));

        PropertyFieldModel cell = rig.Row(PropertyId.LightIntensity).Fields[0];
        cell.BeginEdit();
        cell.Text = "abc";
        cell.Commit();
        Assert.True(cell.HasRejection);

        cell.BeginEdit();
        cell.Text = "4";
        Assert.False(cell.HasRejection);
    }

    [Fact]
    public void A_rejection_survives_a_republish_of_the_same_value()
    {
        // The engine republishes at 30Hz, so clearing on a publish would erase the message at once.
        var rig = new Rig();
        rig.Publish(Number(PropertyId.LightIntensity, "Intensity", 40f));

        PropertyFieldModel cell = rig.Row(PropertyId.LightIntensity).Fields[0];
        cell.BeginEdit();
        cell.Text = "abc";
        cell.Commit();

        rig.Publish(Number(PropertyId.LightIntensity, "Intensity", 40f));
        Assert.True(cell.HasRejection);

        // A value that really moved makes the message stale.
        rig.Publish(Number(PropertyId.LightIntensity, "Intensity", 12f));
        Assert.False(cell.HasRejection);
    }

    [Fact]
    public void Escape_clears_a_rejection_with_the_edit()
    {
        var rig = new Rig();
        rig.Publish(Number(PropertyId.LightIntensity, "Intensity", 40f));

        PropertyFieldModel cell = rig.Row(PropertyId.LightIntensity).Fields[0];
        cell.BeginEdit();
        cell.Text = "abc";
        cell.Commit();

        cell.Revert();

        Assert.False(cell.HasRejection);
        Assert.Equal("40", cell.Text);
    }

    [Fact]
    public void The_row_reports_its_first_refused_cell()
    {
        var rig = new Rig();
        rig.Publish(Vector(PropertyId.Position, new Vector3(1f, 2f, 3f)));

        PropertyRowModel row = rig.Row(PropertyId.Position);
        Assert.False(row.HasRejection);

        PropertyFieldModel y = row.Fields[1];
        y.BeginEdit();
        y.Text = "up a bit";
        y.Commit();

        Assert.True(row.HasRejection);
        Assert.Contains("y", row.Rejection);
    }

    [Fact]
    public void A_picker_drag_writes_the_whole_colour_every_time()
    {
        // Many edits inside one gesture, each carrying the whole vector.
        var rig = new Rig();
        rig.Publish(new PropertyRow
        {
            Group = "Light", Name = "Color", Id = PropertyId.LightColor, Kind = PropertyKind.Color,
            Vector = new Vector3(1f, 1f, 1f), Choices = [], PresentCount = 1, SelectionCount = 1,
        });

        PropertyRowModel row = rig.Row(PropertyId.LightColor);
        rig.Panel.BeginGesture("Color");
        row.ScrubColor(new Vector3(0.5f, 0.25f, 0.125f));
        row.ScrubColor(new Vector3(0.6f, 0.25f, 0.125f));
        rig.Panel.EndGesture(commit: true);

        Assert.Equal(2, rig.Edits.Count);
        Assert.All(rig.Edits, e => Assert.Equal(PropertyAxes.All, e.Axes));
        Assert.Equal(new Vector3(0.6f, 0.25f, 0.125f), rig.Edits[1].Vector);

        Assert.Equal(["Color"], rig.GesturesOpened);
        Assert.Equal([true], rig.GesturesClosed);
    }

    [Fact]
    public void A_picker_drag_shows_its_colour_without_waiting_for_the_engine()
    {
        var rig = new Rig();
        rig.Publish(new PropertyRow
        {
            Group = "Light", Name = "Color", Id = PropertyId.LightColor, Kind = PropertyKind.Color,
            Vector = Vector3.One, Choices = [], PresentCount = 1, SelectionCount = 1,
        });

        PropertyRowModel row = rig.Row(PropertyId.LightColor);
        row.ScrubColor(Vector3.Zero);

        Assert.Equal("#000000", row.Hex);
    }

    [Fact]
    public void A_mixed_colour_row_offers_the_picker_a_NaN_to_open_on()
    {
        var rig = new Rig();
        rig.Publish(2, new PropertyRow
        {
            Group = "Light", Name = "Color", Id = PropertyId.LightColor, Kind = PropertyKind.Color,
            Vector = Vector3.One, Choices = [], PresentCount = 2, SelectionCount = 2,
            MixedAxes = PropertyAxes.All,
        });

        PropertyRowModel row = rig.Row(PropertyId.LightColor);

        Assert.True(row.IsColorMixed);
        Assert.True(float.IsNaN(row.ColorLinear.X));
    }

    [Fact]
    public void A_choice_shows_its_word_and_commits_its_token()
    {
        // The map format stores "World"; the editor calls it "Block".
        var rig = new Rig();
        rig.Publish(new PropertyRow
        {
            Group = "Brush", Name = "Kind", Id = PropertyId.BrushKind, Kind = PropertyKind.Choice,
            Text = "World", Choices = ["World", "Part"], ChoiceLabels = ["Block", "Part"],
            Help = "Block: fused into the level.", PresentCount = 1, SelectionCount = 1,
        });

        PropertyRowModel row = rig.Row(PropertyId.BrushKind);
        Assert.Equal("Block", row.ChoiceLabel);
        Assert.Equal(["Block", "Part"], row.ChoiceLabels);
        Assert.True(row.HasHelp);

        row.ChoiceLabel = "Part";

        PropertyEdit edit = Assert.Single(rig.Edits);
        Assert.Equal("Part", edit.Text);
    }

    [Fact]
    public void Picking_a_word_writes_the_token_that_is_not_the_word()
    {
        var rig = new Rig();
        rig.Publish(new PropertyRow
        {
            Group = "Brush", Name = "Operation", Id = PropertyId.BrushOperation,
            Kind = PropertyKind.Choice, Text = "Additive",
            Choices = ["Additive", "Subtractive"], ChoiceLabels = ["Adds solid", "Cuts solid"],
            PresentCount = 1, SelectionCount = 1,
        });

        rig.Row(PropertyId.BrushOperation).ChoiceLabel = "Cuts solid";

        Assert.Equal("Subtractive", Assert.Single(rig.Edits).Text);
    }

    [Fact]
    public void A_row_with_no_labels_shows_its_tokens()
    {
        var rig = new Rig();
        rig.Publish(Choice(PropertyId.LightKind, "Point", "Point", "Spot"));

        PropertyRowModel row = rig.Row(PropertyId.LightKind);
        Assert.Equal("Point", row.ChoiceLabel);
        Assert.Equal(row.Choices, row.ChoiceLabels);
        Assert.False(row.HasHelp);
    }

    [Fact]
    public void A_refresh_does_not_post_a_word_back()
    {
        var rig = new Rig();
        rig.Publish(new PropertyRow
        {
            Group = "Brush", Name = "Kind", Id = PropertyId.BrushKind, Kind = PropertyKind.Choice,
            Text = "World", Choices = ["World", "Part"], ChoiceLabels = ["Block", "Part"],
            PresentCount = 1, SelectionCount = 1,
        });

        rig.Publish(new PropertyRow
        {
            Group = "Brush", Name = "Kind", Id = PropertyId.BrushKind, Kind = PropertyKind.Choice,
            Text = "Part", Choices = ["World", "Part"], ChoiceLabels = ["Block", "Part"],
            PresentCount = 1, SelectionCount = 1,
        });

        Assert.Empty(rig.Edits);
        Assert.Equal("Part", rig.Row(PropertyId.BrushKind).ChoiceLabel);
    }
}

/// <summary>
/// An asset row: a file chosen from the project rather than a value typed into
/// a box.
/// </summary>
public sealed class AssetRowTests
{
    private sealed class Rig
    {
        public List<PropertyEdit> Edits { get; } = [];
        public PropertyPanelModel Panel { get; }

        public Rig() => Panel = new PropertyPanelModel(Edits.Add, _ => { }, _ => { });

        public void Publish(params PropertyRow[] rows) => Panel.Apply(rows, 1);

        public PropertyRowModel Row(PropertyId id) =>
            Panel.Groups.SelectMany(g => g.Rows).Single(r => r.Id == id);
    }

    private static PropertyRow Asset(
        PropertyId id, string path, string note = "", string key = "", bool mixed = false) =>
        new()
        {
            Group = "Material", Name = "Material", Id = id, Key = key,
            Kind = PropertyKind.Asset, Asset = AssetKind.Material,
            Text = path, Note = note, Choices = [],
            PresentCount = 1, SelectionCount = 1,

            // A one-cell row is mixed when the mask is All.
            MixedAxes = mixed ? PropertyAxes.All : PropertyAxes.None,
        };

    [Fact]
    public void An_asset_row_shows_the_stem_and_keeps_the_path()
    {
        var rig = new Rig();
        rig.Publish(Asset(PropertyId.BrushMaterial, "Materials/dev/wall_brick_02.spectramat"));

        PropertyRowModel row = rig.Row(PropertyId.BrushMaterial);

        row.IsAsset.ShouldBeTrue();
        row.AssetKind.ShouldBe(AssetKind.Material);

        row.AssetLabel.ShouldBe("wall_brick_02");
        row.AssetPath.ShouldBe("Materials/dev/wall_brick_02.spectramat");
    }

    [Fact]
    public void An_empty_path_reads_as_the_engine_default()
    {
        var rig = new Rig();
        rig.Publish(Asset(PropertyId.BrushMaterial, ""));

        rig.Row(PropertyId.BrushMaterial).AssetLabel.ShouldBe("(default)");
    }

    [Fact]
    public void A_mixed_selection_says_so_instead_of_naming_one_of_them()
    {
        var rig = new Rig();
        rig.Publish(Asset(PropertyId.BrushMaterial, "", mixed: true));

        rig.Row(PropertyId.BrushMaterial).AssetLabel.ShouldBe("(mixed)");
    }

    [Fact]
    public void The_note_is_what_says_a_file_is_missing()
    {
        var rig = new Rig();
        rig.Publish(Asset(PropertyId.BrushMaterial, "Materials/gone.spectramat", "missing"));

        PropertyRowModel row = rig.Row(PropertyId.BrushMaterial);

        row.HasNote.ShouldBeTrue();
        row.Note.ShouldBe("missing");
    }

    [Fact]
    public void A_refresh_posts_nothing()
    {
        var rig = new Rig();
        rig.Publish(Asset(PropertyId.BrushMaterial, "Materials/a.spectramat"));
        rig.Publish(Asset(PropertyId.BrushMaterial, "Materials/b.spectramat"));

        rig.Row(PropertyId.BrushMaterial).AssetLabel.ShouldBe("b");
        rig.Edits.ShouldBeEmpty();
    }

    [Fact]
    public void Picking_posts_one_edit_carrying_the_rows_key()
    {
        var rig = new Rig();
        rig.Publish(Asset(PropertyId.FaceMaterial, "Materials/a.spectramat", key: "4"));

        rig.Row(PropertyId.FaceMaterial).PickAsset("Materials/b.spectramat");

        rig.Edits.Count.ShouldBe(1);
        rig.Edits[0].Id.ShouldBe(PropertyId.FaceMaterial);

        // The key is the face's plane index; the editor refuses an edit without one.
        rig.Edits[0].Key.ShouldBe("4");
        rig.Edits[0].Text.ShouldBe("Materials/b.spectramat");
    }

    [Fact]
    public void Picking_nothing_is_a_real_answer_and_posts_an_empty_path()
    {
        var rig = new Rig();
        rig.Publish(Asset(PropertyId.BrushMaterial, "Materials/a.spectramat"));

        rig.Row(PropertyId.BrushMaterial).PickAsset("");

        rig.Edits.Count.ShouldBe(1);
        rig.Edits[0].Text.ShouldBe("");
    }

    [Fact]
    public void A_row_that_is_not_an_asset_refuses_a_pick()
    {
        var rig = new Rig();
        rig.Publish(new PropertyRow
        {
            Group = "Transform", Name = "Position", Id = PropertyId.Position,
            Kind = PropertyKind.Vector3, Choices = [], PresentCount = 1, SelectionCount = 1,
        });

        rig.Row(PropertyId.Position).PickAsset("Materials/a.spectramat");

        rig.Edits.ShouldBeEmpty();
    }
}
