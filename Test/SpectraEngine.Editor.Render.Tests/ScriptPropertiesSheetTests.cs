using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;

using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// A script node in the Properties panel, rasterised so a person can look at
/// it: the File row with its Open button, the note for a file that is gone,
/// and the picker the row opens.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class ScriptPropertiesSheetTests(RibbonSession session) : IDisposable
{
    private readonly ScriptProjectFixture _project = new();

    public void Dispose() => _project.Dispose();

    [Fact]
    public void A_script_nodes_rows_are_a_file_to_pick_an_open_button_and_two_switches()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, _) = OpenProperties(ScriptProjectFixture.Spin);

            // The kind word in the header, no Transform section, the file's
            // name in a cell that opens the picker, and a checkbox for each switch.
            Shown<TextBlock>(panel).ShouldContain(text => text.Text == "Script" && text.FontSize < 12);
            Shown<TextBlock>(panel).ShouldNotContain(text => text.Text == "Transform" || text.Text == "Position");
            AssetCell(panel).GetVisualDescendants().OfType<TextBlock>().First().Text.ShouldBe("Spin");
            Shown<CheckBox>(panel).Select(box => box.IsChecked).ShouldBe([true, false]);

            Button open = OpenButton(panel);
            open.GetVisualDescendants().OfType<TextBlock>().Single().Text.ShouldBe("Open");
            open.Bounds.Height.ShouldBe(AssetCell(panel).Bounds.Height, "the button is as tall as the cell beside it");
            RibbonProbe.BoundsIn(open, panel).Left.ShouldBeGreaterThan(RibbonProbe.BoundsIn(AssetCell(panel), panel).Right);
            Shown<Border>(panel).ShouldNotContain(border => border.Classes.Contains("notebadge"));

            ScriptSheet.Save(window, "script-properties@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void The_open_button_asks_for_the_file_the_row_names()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, _) = OpenProperties(ScriptProjectFixture.Spin);
            var asked = new List<string>();
            panel.ScriptOpenRequested += asked.Add;

            ContentPanelHarness.Click(window, OpenButton(panel));

            asked.ShouldBe([ScriptProjectFixture.Spin]);
            window.Close();
        });
    }

    [Fact]
    public void A_file_the_project_does_not_have_wears_the_missing_note()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, _) = OpenProperties(ScriptProjectFixture.Gone);

            Border note = Shown<Border>(panel).Single(border => border.Classes.Contains("notebadge"));
            note.GetVisualDescendants().OfType<TextBlock>().Single().Text.ShouldBe("missing");

            // A name can still be opened to find out it is gone, and the
            // launcher says so. The button does not hide.
            OpenButton(panel).IsEffectivelyVisible.ShouldBeTrue();

            ScriptSheet.Save(window, "script-properties-missing@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void A_file_row_that_names_a_batch_file_shows_its_whole_name()
    {
        session.On(() =>
        {
            // A level from somebody else can name any file as a script.
            File.WriteAllText(_project.FullPath("Scripts/setup.bat"), "@echo off\n");
            (Window window, PropertiesPanel panel, _) = OpenProperties("Scripts/setup.bat");

            AssetCell(panel).GetVisualDescendants().OfType<TextBlock>().First().Text.ShouldBe("setup.bat");

            ScriptSheet.Save(window, "script-properties-not-a-script@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void A_file_deleted_outside_the_editor_shows_as_missing_once_the_rows_say_so()
    {
        session.On(() =>
        {
            SceneNode node = _project.ScriptUnderAPart(ScriptProjectFixture.Spin);
            (Window window, PropertiesPanel panel, ShellModel model) = OpenProperties(node);
            Shown<Border>(panel).ShouldNotContain(border => border.Classes.Contains("notebadge"));

            File.Delete(_project.FullPath(ScriptProjectFixture.Spin));
            model.Properties.ShouldNotBeNull().Apply(Rows(node), 1);
            Dispatcher.UIThread.RunJobs();

            Shown<Border>(panel).ShouldContain(border => border.Classes.Contains("notebadge"));
            window.Close();
        });
    }

    [Fact]
    public void The_file_cell_opens_a_picker_of_the_projects_scripts()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, _) = OpenProperties(ScriptProjectFixture.Spin);

            AssetPickerView picker = OpenPicker(panel);

            // A script has to name a file, so there is no row for none.
            picker.ShownRows.Select(row => row.ContentPath).ShouldBe(ScriptProjectFixture.Scripts);
            picker.ShownRows.ShouldNotContain(AssetPickerView.None);
            ContentPanelHarness.PlayButtons(picker).ShouldBeEmpty();

            // The file the node has is the row the list opens on.
            ListBox rows = picker.FindControl<ListBox>("Rows").ShouldNotBeNull();
            rows.SelectedItem.ShouldBeOfType<AssetPickerRow>().ContentPath.ShouldBe(ScriptProjectFixture.Spin);

            ScriptSheet.Save(TopLevel.GetTopLevel(picker).ShouldNotBeNull(), "script-picker@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void A_path_typed_into_the_picker_can_be_taken_for_a_file_that_is_not_there_yet()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, List<PropertyEdit> edits) =
                OpenProperties(ScriptProjectFixture.Spin);
            AssetPickerView picker = OpenPicker(panel);
            TextBox query = picker.FindControl<TextBox>("Query").ShouldNotBeNull();

            query.Text = @"Scripts\traps\Spikes.luau";
            Dispatcher.UIThread.RunJobs();

            AssetPickerRow typed = picker.ShownRows.ShouldHaveSingleItem();
            typed.ContentPath.ShouldBe("Scripts/traps/Spikes.luau");
            typed.Folder.ShouldBe(AssetPickerView.TypedPathNote);
            ScriptSheet.Save(TopLevel.GetTopLevel(picker).ShouldNotBeNull(), "script-picker-typed@2x.png");

            picker.TakeSelected();
            Dispatcher.UIThread.RunJobs();

            edits.ShouldHaveSingleItem().Text.ShouldBe("Scripts/traps/Spikes.luau");
            window.Close();
        });
    }

    [Theory]
    [InlineData("Scripts/Do.luau", "Scripts/Door.luau")]
    [InlineData("Scripts/Spn.luau", ScriptProjectFixture.Spin)]
    [InlineData("Scripts/shared/Tim.luau", "Scripts/shared/Timer.luau")]
    public void Enter_on_a_whole_path_takes_that_path_and_not_a_file_that_holds_its_letters(
        string typed, string lookalike)
    {
        // The search matches letters in order, so it finds the longer name too.
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, List<PropertyEdit> edits) =
                OpenProperties(ScriptProjectFixture.Spin);
            AssetPickerView picker = OpenPicker(panel);

            picker.FindControl<TextBox>("Query").ShouldNotBeNull().Text = typed;
            Dispatcher.UIThread.RunJobs();

            picker.ShownRows.Select(row => row.ContentPath).ShouldBe([typed, lookalike]);
            picker.ShownRows[0].Folder.ShouldBe(AssetPickerView.TypedPathNote);
            picker.FindControl<ListBox>("Rows").ShouldNotBeNull().SelectedIndex.ShouldBe(0);

            if (lookalike == "Scripts/Door.luau")
                ScriptSheet.Save(TopLevel.GetTopLevel(picker).ShouldNotBeNull(), "script-picker-typed-first@2x.png");

            picker.TakeSelected();
            Dispatcher.UIThread.RunJobs();

            edits.ShouldHaveSingleItem().Text.ShouldBe(typed);
            window.Close();
        });
    }

    [Fact]
    public void A_whole_path_that_names_a_file_takes_that_file_and_not_the_one_the_row_holds()
    {
        session.On(() =>
        {
            // The node's own file holds every letter of the path typed for
            // Spin.luau, so it is listed too, and the list opens on it.
            const string held = "Scripts/A_Spins.luau";
            File.WriteAllText(_project.FullPath(held), "print(1)\n");
            (Window window, PropertiesPanel panel, List<PropertyEdit> edits) = OpenProperties(held);
            AssetPickerView picker = OpenPicker(panel);

            picker.FindControl<TextBox>("Query").ShouldNotBeNull().Text = "scripts/spin.luau";
            Dispatcher.UIThread.RunJobs();

            picker.ShownRows.Select(row => row.ContentPath).ShouldBe([ScriptProjectFixture.Spin, held]);
            picker.FindControl<ListBox>("Rows").ShouldNotBeNull().SelectedIndex.ShouldBe(0);

            picker.TakeSelected();
            Dispatcher.UIThread.RunJobs();

            edits.ShouldHaveSingleItem().Text.ShouldBe(ScriptProjectFixture.Spin);
            window.Close();
        });
    }

    [Fact]
    public void A_path_typed_in_another_case_is_offered_as_the_file_it_is()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, List<PropertyEdit> edits) =
                OpenProperties(ScriptProjectFixture.Spin);
            AssetPickerView picker = OpenPicker(panel);
            TextBox query = picker.FindControl<TextBox>("Query").ShouldNotBeNull();

            query.Text = "scripts/door.LUAU";
            Dispatcher.UIThread.RunJobs();

            // The file's own spelling, and no second row for the typed one.
            picker.ShownRows.Select(row => row.ContentPath).ShouldBe(["Scripts/Door.luau"]);

            picker.TakeSelected();
            Dispatcher.UIThread.RunJobs();

            edits.ShouldHaveSingleItem().Text.ShouldBe("Scripts/Door.luau");
            window.Close();
        });
    }

    [Theory]
    [InlineData(@"Scripts\Door.luau")]
    [InlineData(@"\scripts\DOOR.luau")]
    [InlineData("./Scripts//Door.luau ")]
    public void A_path_typed_with_other_slashes_is_offered_as_the_file_it_is(string typed)
    {
        // The search goes by the text as typed and finds nothing for these.
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, List<PropertyEdit> edits) =
                OpenProperties(ScriptProjectFixture.Spin);
            AssetPickerView picker = OpenPicker(panel);

            picker.FindControl<TextBox>("Query").ShouldNotBeNull().Text = typed;
            Dispatcher.UIThread.RunJobs();

            AssetPickerRow row = picker.ShownRows.ShouldHaveSingleItem();
            row.ContentPath.ShouldBe("Scripts/Door.luau");
            row.Folder.ShouldBe("Scripts", "it is the file's row, not a path that is no file yet");
            picker.FindControl<TextBlock>("Footer").ShouldNotBeNull().IsVisible.ShouldBeFalse();

            picker.TakeSelected();
            Dispatcher.UIThread.RunJobs();

            edits.ShouldHaveSingleItem().Text.ShouldBe("Scripts/Door.luau");
            window.Close();
        });
    }

    [Fact]
    public void A_search_by_name_takes_the_best_match_on_enter()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, List<PropertyEdit> edits) =
                OpenProperties(ScriptProjectFixture.Spin);
            AssetPickerView picker = OpenPicker(panel);

            picker.FindControl<TextBox>("Query").ShouldNotBeNull().Text = "tim";
            Dispatcher.UIThread.RunJobs();
            picker.TakeSelected();

            edits.ShouldHaveSingleItem().Text.ShouldBe("Scripts/shared/Timer.luau");
            window.Close();
        });
    }

    [Fact]
    public void An_untouched_picker_for_a_missing_file_changes_nothing_on_enter()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, List<PropertyEdit> edits) =
                OpenProperties(ScriptProjectFixture.Gone);
            AssetPickerView picker = OpenPicker(panel);

            // No row is the current file, and there is no row for none to rest on.
            picker.FindControl<ListBox>("Rows").ShouldNotBeNull().SelectedIndex.ShouldBe(-1);
            picker.TakeSelected();

            edits.ShouldBeEmpty();
            window.Close();
        });
    }

    [Fact]
    public void A_search_that_finds_nothing_says_a_path_can_be_typed()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, _) = OpenProperties(ScriptProjectFixture.Spin);
            AssetPickerView picker = OpenPicker(panel);

            picker.FindControl<TextBox>("Query").ShouldNotBeNull().Text = "zzqx";
            Dispatcher.UIThread.RunJobs();

            picker.ShownRows.ShouldBeEmpty();
            TextBlock footer = picker.FindControl<TextBlock>("Footer").ShouldNotBeNull();
            footer.IsVisible.ShouldBeTrue();
            footer.Text.ShouldBe("Nothing matches. A path that ends in .luau can be typed in full.");
            window.Close();
        });
    }

    private (Window Window, PropertiesPanel Panel, List<PropertyEdit> Edits) OpenProperties(string file)
    {
        var edits = new List<PropertyEdit>();
        (Window window, PropertiesPanel panel, _) = OpenProperties(_project.ScriptUnderAPart(file), edits);
        return (window, panel, edits);
    }

    private (Window Window, PropertiesPanel Panel, ShellModel Model) OpenProperties(
        SceneNode node, List<PropertyEdit>? edits = null)
    {
        edits ??= [];
        var panelModel = new PropertyPanelModel(edits.Add, _ => { }, _ => { });
        var model = new ShellModel { Properties = panelModel, Assets = new AssetCatalog(NullLogger.Instance) };
        model.Assets.Rebuild(_project.Root);
        panelModel.Apply(Rows(node), 1);

        var panel = new PropertiesPanel { DataContext = model };
        var window = new Window { Content = panel, Width = 308, Height = 260 };
        window.SetRenderScaling(2.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, panel, model);
    }

    // Rows as the engine publishes them for one selected script node.
    private static PropertyRow[] Rows(SceneNode node)
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(node, rows);
        return [.. rows];
    }

    private static AssetPickerView OpenPicker(PropertiesPanel panel)
    {
        AssetCell(panel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        return panel.FindControl<AssetPickerView>("AssetPicker").ShouldNotBeNull();
    }

    // The cell that names the file and opens the picker.
    private static Button AssetCell(PropertiesPanel panel) =>
        Shown<Button>(panel).Single(button => button.Classes.Contains("assetcell") && !button.Classes.Contains("open"));

    private static Button OpenButton(PropertiesPanel panel) =>
        Shown<Button>(panel).Single(button => button.Classes.Contains("open"));

    private static T[] Shown<T>(PropertiesPanel panel) where T : Control => ScriptSheet.Shown<T>(panel);
}
