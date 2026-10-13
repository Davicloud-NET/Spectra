using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The Tags and Attributes sections driven as a person drives them: a box is
/// entered, keys are pressed, a button is clicked, a box is left. Every test
/// goes through the view's own handlers.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class TagsAndAttributesKeysTests(RibbonSession session)
{
    [Fact]
    public void Enter_in_the_tag_field_adds_a_chip_and_the_remove_button_takes_it_off()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            using var sheet = new TagSheet(null, brick);

            sheet.Enter(sheet.Panel.Tags.AddField);
            sheet.Keys("Deep");
            sheet.Press(PhysicalKey.Enter);
            sheet.Publish();

            brick.Tags.ShouldBe(["Lava", "Hot stuff", "Checkpoint 3", "Deep"]);
            sheet.Shown<TextBlock>().Where(text => text.Classes.Contains("tagname")).Select(text => text.Text)
                .ShouldBe(["Lava", "Hot stuff", "Checkpoint 3", "Deep"]);
            sheet.Box(sheet.Panel.Tags.AddField).Text.ShouldBeNullOrEmpty();

            sheet.Click(sheet.Shown<Button>().First(button => button.DataContext is TagChipModel { Tag: "Lava" }));
            sheet.Publish();

            brick.Tags.ShouldBe(["Hot stuff", "Checkpoint 3", "Deep"]);
            sheet.Shown<Border>().Count(chip => chip.Classes.Contains("tagchip")).ShouldBe(3);
            sheet.Undo.UndoCount.ShouldBe(2);
        });
    }

    [Fact]
    public void Leaving_the_tag_field_adds_nothing_and_keeps_what_was_typed_for_Enter()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            using var sheet = new TagSheet(null, brick);

            sheet.Enter(sheet.Panel.Tags.AddField);
            sheet.Keys("Deep");
            sheet.Enter(sheet.Panel.Attributes.NewNameField);
            sheet.Publish();

            brick.Tags.ShouldBe(["Lava", "Hot stuff", "Checkpoint 3"]);
            sheet.Box(sheet.Panel.Tags.AddField).Text.ShouldBe("Deep");

            sheet.Box(sheet.Panel.Tags.AddField).Focus();
            sheet.Press(PhysicalKey.Enter);
            sheet.Publish();

            brick.Tags.ShouldBe(["Lava", "Hot stuff", "Checkpoint 3", "Deep"]);
        });
    }

    [Fact]
    public void A_new_attribute_waits_for_its_kind_and_the_Add_button_adds_it()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            using var sheet = new TagSheet(null, brick);

            sheet.Enter(sheet.Panel.Attributes.NewNameField);
            sheet.Keys("Score");

            // To the kind beside it: the name is not added on the way.
            ComboBox kind = sheet.Shown<ComboBox>().Last();
            kind.Focus().ShouldBeTrue();
            sheet.Publish();
            brick.Attributes.Contains("Score").ShouldBeFalse();

            kind.SelectedItem = "Number";
            sheet.Click(sheet.Shown<Button>().Single(button => button.Content as string == "Add"));
            sheet.Publish();

            brick.Attributes.TryGet("Score", out MemberValue score).ShouldBeTrue();
            score.ShouldBe(MemberValue.Of(0d));
            sheet.Box(sheet.Panel.Attributes.NewNameField).Text.ShouldBeNullOrEmpty();
            sheet.Undo.UndoCount.ShouldBe(1);
        });
    }

    [Fact]
    public void Leaving_a_value_cell_keeps_what_was_typed_in_it()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            using var sheet = new TagSheet(null, brick);

            sheet.Enter(Cell(sheet, "Damage"));
            sheet.Keys("40");
            sheet.Enter(Cell(sheet, "Label"));
            sheet.Publish();

            ValueOf(brick, "Damage").ShouldBe(MemberValue.Of(40d));
            sheet.Box(Cell(sheet, "Damage")).Text.ShouldBe("40");
            sheet.Undo.UndoCount.ShouldBe(1);
        });
    }

    [Fact]
    public void Entering_a_cell_and_leaving_it_with_nothing_typed_changes_nothing()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            var pool = new SceneNode("Pool") { Attributes = AttributeSet.Empty.With("Label", MemberValue.Of("cold")) };
            var ember = new SceneNode("Ember");
            using var sheet = new TagSheet(null, brick, pool, ember);
            sheet.Box(Cell(sheet, "Label")).PlaceholderText.ShouldBe("mixed");

            // Into the cell that shows nothing because the two differ, and on through the next.
            sheet.Enter(Cell(sheet, "Label"));
            sheet.Press(PhysicalKey.Tab);
            sheet.Enter(Cell(sheet, "Damage"));
            sheet.Enter(sheet.Panel.Tags.AddField);
            sheet.Publish();

            ValueOf(brick, "Label").ShouldBe(MemberValue.Of("hot"));
            ValueOf(pool, "Label").ShouldBe(MemberValue.Of("cold"));
            ember.Attributes.Count.ShouldBe(0);
            pool.Attributes.Count.ShouldBe(1);
            sheet.Undo.UndoCount.ShouldBe(0);
        });
    }

    [Fact]
    public void A_cell_somebody_is_typing_in_keeps_its_text_when_the_level_changes_under_it()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            using var sheet = new TagSheet(null, brick);

            sheet.Enter(Cell(sheet, "Label"));
            sheet.Keys("half typ");
            brick.Attributes = brick.Attributes.With("Label", MemberValue.Of("from elsewhere"));
            sheet.Publish();

            sheet.Box(Cell(sheet, "Label")).Text.ShouldBe("half typ");
        });
    }

    [Fact]
    public void Escape_puts_back_what_the_node_holds_and_tells_the_host()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            using var sheet = new TagSheet(null, brick);
            int escapes = 0;
            sheet.Sections.EscapePressed += () => escapes++;

            sheet.Enter(Cell(sheet, "Damage"));
            sheet.Keys("99");
            sheet.Press(PhysicalKey.Escape);
            sheet.Publish();

            escapes.ShouldBe(1);
            sheet.Box(Cell(sheet, "Damage")).Text.ShouldBe("25");
            ValueOf(brick, "Damage").ShouldBe(MemberValue.Of(25d));
            sheet.Undo.UndoCount.ShouldBe(0);
        });
    }

    [Fact]
    public void After_Enter_the_box_shows_the_level_again_until_the_next_key()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            using var sheet = new TagSheet(null, brick);

            sheet.Enter(Cell(sheet, "Damage"));
            sheet.Keys("30");
            sheet.Press(PhysicalKey.Enter);
            sheet.Publish();
            ValueOf(brick, "Damage").ShouldBe(MemberValue.Of(30d));

            // The box kept the caret and is not being typed in: a change from elsewhere shows.
            brick.Attributes = brick.Attributes.With("Damage", MemberValue.Of(31d));
            sheet.Publish();
            sheet.Box(Cell(sheet, "Damage")).Text.ShouldBe("31");

            // A key makes it a box somebody types in again. The press comes
            // before its letter, and Shift is a press with no letter of its own.
            sheet.Press(PhysicalKey.ShiftLeft);
            sheet.Keys("5");
            brick.Attributes = brick.Attributes.With("Damage", MemberValue.Of(32d));
            sheet.Publish();
            sheet.Box(Cell(sheet, "Damage")).Text.ShouldBe("315");

            sheet.Press(PhysicalKey.Enter);
            sheet.Publish();
            ValueOf(brick, "Damage").ShouldBe(MemberValue.Of(315d));
        });
    }

    [Fact]
    public void The_cross_on_a_row_takes_its_attribute_off()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            using var sheet = new TagSheet(null, brick);

            sheet.Click(RowOf(sheet, "Damage").GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("ghost")));
            sheet.Publish();

            brick.Attributes.Contains("Damage").ShouldBeFalse();
            sheet.Panel.Attributes.Rows.Select(row => row.Name).ShouldBe(["Label", "Deadly", "Push", "Glow", "Rest"]);
            sheet.Undo.UndoCount.ShouldBe(1);
        });
    }

    [Fact]
    public void A_colour_picked_from_the_swatch_is_one_undo_entry()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            using var sheet = new TagSheet(null, brick);

            OpenPicker(sheet);
            SetHex(sheet, "#0000FF");
            SetHex(sheet, "#00FF00");
            sheet.Publish();
            ValueOf(brick, "Glow").Color.ShouldBe(new Color3(0f, 1f, 0f));
            sheet.Box(Cell(sheet, "Glow")).Text.ShouldBe("#00FF00");

            PressInHex(sheet, Key.Enter);
            sheet.Publish();

            sheet.Sections.ColorPopup.IsOpen.ShouldBeFalse();
            ValueOf(brick, "Glow").Color.ShouldBe(new Color3(0f, 1f, 0f));
            sheet.Undo.UndoCount.ShouldBe(1);
        });
    }

    [Fact]
    public void Escape_in_the_picker_puts_the_colour_back_and_records_nothing()
    {
        session.On(() =>
        {
            SceneNode brick = TagSheet.Brick();
            using var sheet = new TagSheet(null, brick);
            int escapes = 0;
            sheet.Sections.EscapePressed += () => escapes++;

            OpenPicker(sheet);
            SetHex(sheet, "#0000FF");
            ValueOf(brick, "Glow").Color.ShouldBe(new Color3(0f, 0f, 1f));

            PressInHex(sheet, Key.Escape);
            sheet.Publish();

            sheet.Sections.ColorPopup.IsOpen.ShouldBeFalse();
            ValueOf(brick, "Glow").Color.ShouldBe(new Color3(1f, 0.5f, 0f));
            sheet.Undo.UndoCount.ShouldBe(0);
            escapes.ShouldBe(1);

            // The level is back at the sets the panel saw last, so no news
            // comes. The swatch and its cell still have to show them.
            sheet.Box(Cell(sheet, "Glow")).Text.ShouldBe("#FF8000");
            AttributeRowModel glow = sheet.Panel.Attributes.Rows.Single(row => row.Name == "Glow");
            glow.Swatch.ShouldBeAssignableTo<ISolidColorBrush>().ShouldNotBeNull()
                .Color.ShouldBe(Color.FromRgb(255, 128, 0));
        });
    }

    [Fact]
    public void A_picker_opened_and_closed_with_no_change_records_nothing()
    {
        session.On(() =>
        {
            using var sheet = new TagSheet(null, TagSheet.Brick());

            OpenPicker(sheet);
            sheet.Sections.ColorPopup.IsOpen = false;
            sheet.Publish();

            sheet.Undo.UndoCount.ShouldBe(0);
            sheet.Undo.IsTransactionOpen.ShouldBeFalse();
        });
    }

    // Presses the swatch of the brick's colour, which opens the picker over it.
    private static void OpenPicker(TagSheet sheet)
    {
        Border swatch = RowOf(sheet, "Glow").GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("swatch"));

        sheet.Click(swatch);

        sheet.Sections.ColorPopup.IsOpen.ShouldBeTrue();
        sheet.Sections.ColorPicker.Model.Linear.X.ShouldBe(1f, 1e-3f);
        sheet.Sections.ColorPicker.Model.Linear.Z.ShouldBe(0f, 1e-3f);
    }

    // A colour typed into the picker's own box, which is then left. Each is
    // one step of a drag across the square.
    private static void SetHex(TagSheet sheet, string hex)
    {
        sheet.Sections.ColorPicker.HexBox.Text = hex;
        sheet.Sections.ColorPicker.HexBox.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
    }

    private static void PressInHex(TagSheet sheet, Key key) =>
        sheet.Sections.ColorPicker.HexBox.RaiseEvent(
            new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    private static PropertyFieldModel Cell(TagSheet sheet, string attribute) =>
        sheet.Panel.Attributes.Rows.Single(row => row.Name == attribute).Cells[0];

    // The row's own border, which holds its name, its kind, its cross and its cells.
    private static Control RowOf(TagSheet sheet, string attribute) =>
        sheet.Shown<Border>().Single(border =>
            border.Classes.Contains("proprow") && border.DataContext is AttributeRowModel row && row.Name == attribute);

    private static MemberValue ValueOf(SceneNode node, string name) =>
        node.Attributes.TryGet(name, out MemberValue value) ? value : MemberValue.None;
}
