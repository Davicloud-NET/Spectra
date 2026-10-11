using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;

using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The places a script shows in the shell before the level plays, rasterised
/// so a person can look at them: the Content panel, a script node's rows in
/// Properties, the picker its File row opens, and the ribbon's Script button.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class ScriptSheetTests(RibbonSession session) : IDisposable
{
    private readonly ScriptProjectFixture _project = new();

    public void Dispose() => _project.Dispose();

    [Fact]
    public void The_content_panel_lists_scripts_among_other_files()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);
            ContentBrowserModel browser = model.Content.ShouldNotBeNull();

            browser.Entries.Where(entry => entry.IsScript).Select(entry => entry.Name)
                .ShouldBe(["Door.luau", "Lobby.luau", "Spin.luau"]);
            RowHeights(panel).Distinct().Count().ShouldBe(1, "a script row is as tall as its neighbours");
            ContentPanelHarness.PlayButtons(panel).ShouldBeEmpty("a script has nothing to play");

            Save(window, "script-content-list@2x.png");

            browser.ViewMode = ContentViewMode.Grid;
            Dispatcher.UIThread.RunJobs();
            Save(window, "script-content-grid@2x.png");

            Close(window, model);
        });
    }

    [Fact]
    public void The_scripts_chip_narrows_the_content_panel_to_scripts()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);
            ContentBrowserModel browser = model.Content.ShouldNotBeNull();

            Chip(panel, "Scripts").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            browser.Filter.ShouldBe(ContentFilter.Scripts);
            browser.Entries.Select(entry => entry.Name)
                .ShouldBe(["shared", "Door.luau", "Lobby.luau", "Spin.luau"]);

            await SelectAsync(browser, "Door.luau");
            Dispatcher.UIThread.RunJobs();
            browser.SelectedExtra.ShouldBe("2 lines");
            Save(window, "script-content-chip@2x.png");

            Close(window, model);
        });
    }

    [Fact]
    public void A_panel_too_narrow_for_six_chips_shows_one_that_names_the_kind()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);
            ContentBrowserModel browser = model.Content.ShouldNotBeNull();
            browser.Filter = ContentFilter.Scripts;
            Dispatcher.UIThread.RunJobs();
            Control chips = panel.FindControl<StackPanel>("FilterChips").ShouldNotBeNull();
            Button one = panel.FindControl<Button>("FilterMenuChip").ShouldNotBeNull();

            browser.IsNarrow.ShouldBeFalse("at 760 every chip fits");
            chips.IsEffectivelyVisible.ShouldBeTrue();
            one.IsEffectivelyVisible.ShouldBeFalse();

            // As narrow as the bottom dock gets in the smallest window.
            window.Width = 570;
            Dispatcher.UIThread.RunJobs();

            browser.IsNarrow.ShouldBeTrue();
            chips.IsEffectivelyVisible.ShouldBeFalse();
            one.IsEffectivelyVisible.ShouldBeTrue();
            one.GetVisualDescendants().OfType<TextBlock>().Single().Text.ShouldBe("Scripts");
            SearchBox(panel).Bounds.Width.ShouldBeGreaterThan(100, "the search box must keep room to type in");
            Save(window, "script-content-narrow@2x.png");

            // The list is the same filter, so the files do not change.
            browser.Entries.Select(entry => entry.Name)
                .ShouldBe(["shared", "Door.luau", "Lobby.luau", "Spin.luau"]);

            window.Width = 760;
            Dispatcher.UIThread.RunJobs();

            browser.IsNarrow.ShouldBeFalse("wide again, the chips come back");
            chips.IsEffectivelyVisible.ShouldBeTrue();

            Close(window, model);
        });
    }

    [Theory]
    [InlineData(ContentViewMode.List)]
    [InlineData(ContentViewMode.Grid)]
    public void A_double_click_on_a_script_activates_it_so_the_window_can_open_it(ContentViewMode view)
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(view);
            var activated = new List<ContentEntry>();
            panel.EntryActivated += activated.Add;

            ContentPanelHarness.Click(window, ContentPanelHarness.NameOf(panel, "Spin.luau"), times: 2);

            ContentEntry entry = activated.ShouldHaveSingleItem();
            entry.IsScript.ShouldBeTrue();
            entry.ContentPath.ShouldBe(ScriptProjectFixture.Spin);
            Close(window, model);
        });
    }

    [Fact]
    public void Enter_on_a_selected_row_activates_it_as_a_double_click_does()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);
            var activated = new List<string>();
            panel.EntryActivated += entry => activated.Add(entry.Name);

            ContentPanelHarness.Click(window, ContentPanelHarness.NameOf(panel, "Spin.luau"));
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            activated.ShouldBe(["Spin.luau"]);
            Close(window, model);
        });
    }

    [Fact]
    public void A_scripts_menu_opens_it_or_puts_it_into_the_level()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);
            var opened = new List<string>();
            var inserted = new List<string>();
            panel.EntryActivated += entry => opened.Add(entry.Name);
            panel.InsertRequested += entry => inserted.Add(entry.Name);

            ContextMenu menu = MenuOf(panel, "Spin.luau");
            MenuItem[] items = [.. menu.Items.OfType<MenuItem>()];
            items.Select(item => item.Header).ShouldBe(["Open", "Insert into scene", "Show in Explorer"]);

            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            opened.ShouldBe(["Spin.luau"]);
            inserted.ShouldBe(["Spin.luau"]);
            Close(window, model);
        });
    }

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

            Save(window, "script-properties@2x.png");
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

            Save(window, "script-properties-missing@2x.png");
            window.Close();
        });
    }

    [Fact]
    public void A_file_deleted_outside_the_editor_shows_as_missing_on_the_next_publish()
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

            Save(TopLevel.GetTopLevel(picker).ShouldNotBeNull(), "script-picker@2x.png");
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
            Save(TopLevel.GetTopLevel(picker).ShouldNotBeNull(), "script-picker-typed@2x.png");

            picker.TakeSelected();
            Dispatcher.UIThread.RunJobs();

            edits.ShouldHaveSingleItem().Text.ShouldBe("Scripts/traps/Spikes.luau");
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

    [Fact]
    public void The_scene_trees_menu_inserts_a_script_and_opens_the_one_a_node_carries()
    {
        session.On(() =>
        {
            var panel = new ScenePanel { DataContext = new ShellModel() };
            int inserts = 0;
            var opened = new List<Guid>();
            panel.InsertScriptRequested += () => inserts++;
            panel.OpenScriptRequested += opened.Add;
            var node = new SceneTreeNode(Guid.NewGuid(), "Script") { HasScript = true };

            (Window window, ContextMenu menu) = RightClickARow(panel, node);

            RowItem(menu, "Insert script").IsVisible.ShouldBeTrue();
            RowItem(menu, "Open script").IsVisible.ShouldBeTrue();

            RowItem(menu, "Insert script").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            RowItem(menu, "Open script").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            inserts.ShouldBe(1);
            opened.ShouldBe([node.Id]);

            if (TopLevel.GetTopLevel(menu) is { } popup)
                Save(popup, "script-tree-menu@2x.png");

            menu.Close();
            window.Close();
        });
    }

    [Fact]
    public void A_node_that_carries_no_script_has_none_to_open_in_its_menu()
    {
        session.On(() =>
        {
            var panel = new ScenePanel { DataContext = new ShellModel() };
            var node = new SceneTreeNode(Guid.NewGuid(), "Part");

            (Window window, ContextMenu menu) = RightClickARow(panel, node);

            RowItem(menu, "Insert script").IsVisible.ShouldBeTrue("any node can take a script");
            RowItem(menu, "Open script").IsVisible.ShouldBeFalse();

            // The tree learns of a script from a change, and the open menu follows.
            node.HasScript = true;
            Dispatcher.UIThread.RunJobs();
            RowItem(menu, "Open script").IsVisible.ShouldBeTrue();

            menu.Close();
            window.Close();
        });
    }

    [Fact]
    public void The_ribbons_insert_group_has_a_script_button_under_group()
    {
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open("build", scaling: 2.0);

            Control script = probe.ByTag("insert.script");
            Control group = probe.ByTag("insert.group");
            Rect scriptBox = RibbonProbe.BoundsIn(script, probe.Page);
            Rect groupBox = RibbonProbe.BoundsIn(group, probe.Page);

            script.IsEffectivelyVisible.ShouldBeTrue();
            script.IsEnabled.ShouldBeTrue("nothing has to be selected: with nothing it is a level script");
            script.GetVisualDescendants().OfType<TextBlock>().Single().Text.ShouldBe("Script");
            scriptBox.X.ShouldBe(groupBox.X);
            scriptBox.Y.ShouldBeGreaterThan(groupBox.Y);
            scriptBox.Height.ShouldBe(groupBox.Height);

            // The glyph is the one a script's row and file wear.
            Avalonia.Controls.Shapes.Path glyph =
                script.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            glyph.Data.ShouldBeSameAs(EditorIcons.ScriptGlyph);
            glyph.Stroke.ShouldBe(EditorIcons.Brush(EditorIcons.ForFile(ContentKind.Script)));

            Size want = probe.Body.DesiredSize;
            probe.Window.Width = Math.Ceiling(want.Width);
            probe.Window.Height = Math.Ceiling(want.Height);
            Dispatcher.UIThread.RunJobs();
            Save(probe.Window, "script-ribbon@2x.png");
        });
    }

    [Fact]
    public void While_the_level_plays_the_script_button_greys_with_its_glyph()
    {
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(
                "build", scaling: 2.0,
                model => model.ApplySnapshot(new SpectraEngine.Core.Hosting.FrameSnapshot { IsPlaying = true }));

            Control script = probe.ByTag("insert.script");

            script.IsEnabled.ShouldBeFalse();
            script.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Stroke
                .ShouldBe(Brush("SpectraTextDisabled"));
        });
    }

    private Task<(Window Window, ContentPanel Panel, ShellModel Model)> OpenContentAsync(ContentViewMode view) =>
        ContentPanelHarness.OpenAsync(_project.Root, _project.ScriptsFolder, view);

    private static void Close(Window window, ShellModel model)
    {
        window.Close();
        model.Content.ShouldNotBeNull().SetRoot(null);
    }

    // The details strip is filled off the UI thread, so wait for it to land.
    private static async Task SelectAsync(ContentBrowserModel browser, string name)
    {
        var landed = new TaskCompletionSource();
        browser.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(ContentBrowserModel.SelectedExtra) && browser.SelectedExtra.Length > 0)
                landed.TrySetResult();
        };

        browser.Select(browser.Entries.Single(entry => entry.Name == name));
        await landed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private (Window Window, PropertiesPanel Panel, List<PropertyEdit> Edits)
        OpenProperties(string file)
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

    private static T[] Shown<T>(Visual within) where T : Control =>
        [.. within.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible)];

    private static ContextMenu MenuOf(ContentPanel panel, string name)
    {
        Control row = ContentPanelHarness.NameOf(panel, name);
        for (Visual? visual = row; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control { ContextMenu: { } menu } owner)
            {
                // A menu's rows bind to the row it opened on.
                menu.DataContext = owner.DataContext;
                return menu;
            }
        }

        throw new InvalidOperationException($"no row of {name} carries a menu");
    }

    // Opens the scene tree's row menu on a row that shows this node, the way a
    // right-click does. ContextMenu.Open skips the Opening event, where the
    // panel fills the menu.
    private static (Window Window, ContextMenu Menu) RightClickARow(ScenePanel panel, SceneTreeNode node)
    {
        var menu = panel.Resources["RowMenu"].ShouldBeOfType<ContextMenu>();
        var row = new Border { Width = 200, Height = 22, ContextMenu = menu, DataContext = node };

        var window = new Window { Content = new StackPanel { Children = { row, panel } }, Width = 320, Height = 400 };
        window.SetRenderScaling(2.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        row.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        menu.IsOpen.ShouldBeTrue("the right-click should have opened the row menu");
        return (window, menu);
    }

    private static MenuItem RowItem(ContextMenu menu, string header) =>
        menu.Items.OfType<MenuItem>().Single(item => item.Header as string == header);

    private static Avalonia.Media.IBrush? Brush(string key) =>
        Application.Current.ShouldNotBeNull().TryFindResource(key, out object? value)
            ? value as Avalonia.Media.IBrush
            : null;

    private static IEnumerable<double> RowHeights(ContentPanel panel) =>
        panel.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(row => row.IsEffectivelyVisible)
            .Select(row => row.Bounds.Height);

    private static Button Chip(ContentPanel panel, string tag) =>
        panel.GetVisualDescendants().OfType<Button>().Single(button => button.Tag as string == tag);

    private static TextBox SearchBox(ContentPanel panel) =>
        panel.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "SearchBox");

    // Two ticks: a change made while the last one waits to be drawn is only
    // handed to the renderer once that one has been.
    private static void Save(TopLevel surface, string fileName)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        WriteableBitmap frame = surface.GetLastRenderedFrame().ShouldNotBeNull("nothing was rasterised");

        Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
        frame.Save(Path.Combine(RibbonSheetTests.OutputDirectory, fileName), new PngBitmapEncoderOptions());
    }
}
