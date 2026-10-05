using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using SpectraEngine.Core.Entities;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The entity lists a mapper picks a class from: the Insert list, the Make
/// entity list, and the Scene panel's row menu that carries the second one.
/// Each lands as a sheet for a person to read.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class EntityMenuSheetTests(RibbonSession session)
{
    // Fixture classes, one of every placement. They exist only as .sentdef
    // bytes, the way a session's catalogue does.
    private static List<EntityInsertItem> Classes() => EntityInsertMenu.Build(
        EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
        [
            new EntitySchema("func_door", "Door", "Movers", EntityPlacement.Brush),
            new EntitySchema("info_player_start", "Player start", "Info", EntityPlacement.Point),
            new EntitySchema("logic_relay", "Relay", "Logic", EntityPlacement.Abstract),
            new EntitySchema("trigger_once", "Trigger (once)", "Triggers", EntityPlacement.Volume),
        ])));

    private static ShellModel ModelWithClasses()
    {
        var model = new ShellModel();
        model.SetEntityClasses(Classes());
        return model;
    }

    private static List<string?> Headers(ItemCollection rows) =>
        rows.OfType<MenuItem>().Select(row => row.Header as string).ToList();

    [Fact]
    public void The_insert_list_shows_every_class()
    {
        session.On(() =>
        {
            ShellModel model = ModelWithClasses();
            var list = new MenuFlyoutPresenter();
            EntityInsertMenu.Fill(list.Items, model.EntityClasses, forMake: false, _ => { });

            Headers(list.Items).ShouldBe(["Door", "Player start", "Relay", "Trigger (once)"]);
            Save(list, "menu-insert-entity@2x.png");
        });
    }

    [Fact]
    public void The_make_entity_list_shows_the_classes_made_from_geometry()
    {
        session.On(() =>
        {
            ShellModel model = ModelWithClasses();
            var list = new MenuFlyoutPresenter();
            EntityInsertMenu.Fill(list.Items, model.MakeEntityClasses, forMake: true, _ => { });

            Headers(list.Items).ShouldBe(["Door", "Trigger (once)"]);
            Save(list, "menu-make-entity@2x.png");
        });
    }

    [Fact]
    public void A_row_names_the_class_it_was_built_for_when_it_is_clicked()
    {
        session.On(() =>
        {
            var picked = new List<string>();
            var list = new MenuFlyoutPresenter();
            EntityInsertMenu.Fill(list.Items, Classes(), forMake: true, picked.Add);

            foreach (MenuItem row in list.Items.OfType<MenuItem>())
                row.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            picked.ShouldBe(["func_door", "info_player_start", "logic_relay", "trigger_once"]);
        });
    }

    [Fact]
    public void The_scene_panels_row_menu_fills_make_entity_as_it_opens()
    {
        // An item with no rows has no submenu to open, so the rows have to be
        // there before the menu shows.
        session.On(() =>
        {
            var picked = new List<string>();
            var panel = new ScenePanel { DataContext = ModelWithClasses() };
            panel.MakeEntityRequested += picked.Add;
            int removes = 0;
            panel.RemoveEntityRequested += () => removes++;

            (Window window, ContextMenu menu) = RightClickARow(panel, before: m => Make(m).Items.ShouldBeEmpty());

            Headers(menu.Items).ShouldBe(
            [
                "Rename", "Frame in viewport", "Duplicate", "Delete", "Group", "Ungroup",
                "Convert block / part", "Make entity", "Remove entity", "Expand all", "Collapse all",
            ]);
            Make(menu).IsVisible.ShouldBeTrue();
            Headers(Make(menu).Items).ShouldBe(["Door", "Trigger (once)"]);

            Make(menu).Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            picked.ShouldBe(["func_door"]);

            menu.Items.OfType<MenuItem>().Single(i => (i.Header as string) == "Remove entity")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            removes.ShouldBe(1);

            if (TopLevel.GetTopLevel(menu) is { } popup)
                SaveFrame(popup, "menu-scene-row.png");

            menu.Close();
            window.Close();
        });
    }

    [Fact]
    public void A_project_with_no_geometry_class_hides_make_entity_in_the_row_menu()
    {
        session.On(() =>
        {
            var model = new ShellModel();
            model.SetEntityClasses(EntityInsertMenu.Build(EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
            [
                new EntitySchema("logic_relay", placement: EntityPlacement.Abstract),
            ]))));

            (Window window, ContextMenu menu) = RightClickARow(new ScenePanel { DataContext = model });

            Make(menu).IsVisible.ShouldBeFalse();

            menu.Close();
            window.Close();
        });
    }

    private static MenuItem Make(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().Single(i => i.Name == "MakeEntityItem");

    // Opens the row menu the way a right-click does. ContextMenu.Open skips
    // the Opening event, and that is where the panel fills the list.
    private static (Window Window, ContextMenu Menu) RightClickARow(
        ScenePanel panel, Action<ContextMenu>? before = null)
    {
        var menu = panel.Resources["RowMenu"].ShouldBeOfType<ContextMenu>();
        var row = new Border { Width = 200, Height = 22, ContextMenu = menu };

        var window = new Window { Content = new StackPanel { Children = { row, panel } }, Width = 320, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        before?.Invoke(menu);

        row.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        menu.IsOpen.ShouldBeTrue("the right-click should have opened the row menu");
        return (window, menu);
    }

    private static void Save(Control content, string fileName)
    {
        var window = new Window { Content = content, SizeToContent = SizeToContent.WidthAndHeight };
        window.SetRenderScaling(2.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        SaveFrame(window, fileName).ShouldBeTrue("the list rasterised no frame at all");
        window.Close();
    }

    private static bool SaveFrame(TopLevel surface, string fileName)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        WriteableBitmap? frame = surface.GetLastRenderedFrame();
        if (frame is null)
            return false;

        Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
        frame.Save(Path.Combine(RibbonSheetTests.OutputDirectory, fileName), new PngBitmapEncoderOptions());
        return true;
    }
}
