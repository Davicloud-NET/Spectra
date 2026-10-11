using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The places a script file and Insert Script show in the shell, rasterised
/// so a person can look at them: the Content panel, the scene tree's menu and
/// the ribbon's Script button. ScriptPropertiesSheetTests has the Properties
/// panel.
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

            ScriptSheet.Save(window, "script-content-list@2x.png");

            browser.ViewMode = ContentViewMode.Grid;
            Dispatcher.UIThread.RunJobs();
            ScriptSheet.Save(window, "script-content-grid@2x.png");

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
            ScriptSheet.Save(window, "script-content-chip@2x.png");

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
            ScriptSheet.Save(window, "script-content-narrow@2x.png");

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
    public void Enter_on_a_folder_opens_it_and_hands_nothing_to_the_window()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);
            ContentBrowserModel browser = model.Content.ShouldNotBeNull();
            var activated = new List<string>();
            panel.EntryActivated += entry => activated.Add(entry.Name);

            ContentPanelHarness.Click(window, ContentPanelHarness.NameOf(panel, "shared"));
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            browser.Entries.Select(entry => entry.Name).ShouldBe(["Timer.luau"]);
            activated.ShouldBeEmpty();
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
                ScriptSheet.Save(popup, "script-tree-menu@2x.png");

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
            ScriptSheet.Save(probe.Window, "script-ribbon@2x.png");
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
}
