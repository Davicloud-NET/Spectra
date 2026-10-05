using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;

using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The places a sound file shows in the shell, rasterised so a person can
/// look at them: the Content panel, a sound entity's row in Properties, and
/// the picker that row opens.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class SoundSheetTests(RibbonSession session) : IDisposable
{
    private readonly SoundProjectFixture _project = new();

    public void Dispose() => _project.Dispose();

    [Fact]
    public void The_content_panel_lists_sounds_among_other_files()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);

            PlayButtons(panel).Select(button => button.ContentPath).ShouldBe(
                [SoundProjectFixture.DoorClose, SoundProjectFixture.DoorOpen, "Sounds/guard_hey.wav", "Sounds/lift_hum.wav"],
                ignoreOrder: true);
            RowHeights(panel).Distinct().Count().ShouldBe(1, "a sound row is as tall as its neighbours");

            Save(window, "content-all-list@2x.png");
            window.Close();
            model.Content.ShouldNotBeNull().SetRoot(null);
        });
    }

    [Fact]
    public void The_sounds_chip_narrows_the_content_panel_to_sounds()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);
            ContentBrowserModel browser = model.Content.ShouldNotBeNull();

            Chip(panel, "Sounds").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            browser.Filter.ShouldBe(ContentFilter.Sounds);
            browser.Entries.Select(entry => entry.Name).ShouldBe(
                ["ambience", "door_close.wav", "door_open.wav", "guard_hey.wav", "lift_hum.wav"]);

            // One file selected for the details strip, and another playing.
            await SelectAsync(browser, "lift_hum.wav");
            model.SoundPreview.Apply(SoundProjectFixture.DoorClose);
            Dispatcher.UIThread.RunJobs();

            browser.SelectedExtra.ShouldBe("0.008 s  stereo  48000 Hz  loop region");
            PlayButtons(panel).Count(button => button.ShowsStop).ShouldBe(1);
            Save(window, "content-sounds-list@2x.png");

            browser.ViewMode = ContentViewMode.Grid;
            Dispatcher.UIThread.RunJobs();
            PlayButtons(panel).Count().ShouldBe(4);
            Save(window, "content-sounds-grid@2x.png");

            // As narrow as the bottom dock gets in the smallest window.
            browser.ViewMode = ContentViewMode.List;
            window.Width = 570;
            Dispatcher.UIThread.RunJobs();
            SearchBox(panel).Bounds.Width.ShouldBeGreaterThan(100, "the chips must leave the search box room to type in");
            Save(window, "content-sounds-narrow@2x.png");

            window.Close();
            browser.SetRoot(null);
        });
    }

    [Fact]
    public void In_a_search_the_play_buttons_line_up_and_so_do_the_names()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);
            ContentBrowserModel browser = model.Content.ShouldNotBeNull();

            // Files from two folders, so the folder labels differ in width.
            browser.Query = "w";
            Dispatcher.UIThread.RunJobs();

            browser.Entries.Select(entry => entry.FolderLabel).Distinct().Count().ShouldBeGreaterThan(1);
            browser.Entries.ShouldContain(entry => !entry.IsSound, "the search should mix sounds with other files");
            SoundPreviewButton[] buttons = [.. PlayButtons(panel)];
            buttons.Length.ShouldBeGreaterThan(2);
            buttons.Select(button => RibbonProbe.BoundsIn(button, panel).X).Distinct().Count().ShouldBe(1);
            NameLefts(panel).Length.ShouldBe(1, "a row with no play button starts its name where the others do");

            Save(window, "content-search-list@2x.png");
            window.Close();
            browser.SetRoot(null);
        });
    }

    [Fact]
    public void A_listing_with_no_sound_keeps_no_room_for_a_play_button()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);
            ContentBrowserModel browser = model.Content.ShouldNotBeNull();
            double withSounds = NameLefts(panel).ShouldHaveSingleItem();

            browser.Filter = ContentFilter.Textures;
            Dispatcher.UIThread.RunJobs();

            browser.HasSounds.ShouldBeFalse();
            NameLefts(panel).ShouldHaveSingleItem().ShouldBeLessThan(withSounds);
            Save(window, "content-textures-list@2x.png");

            window.Close();
            browser.SetRoot(null);
        });
    }

    [Fact]
    public void A_clicked_sound_row_keeps_its_play_glyph_the_colour_of_its_name()
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenContentAsync(ContentViewMode.List);
            ContentPanelHarness.Click(window, ContentPanelHarness.NameOf(panel, "door_open.wav"));
            model.SoundPreview.Apply(SoundProjectFixture.DoorClose);
            Dispatcher.UIThread.RunJobs();

            SoundPreviewButton button = ContentPanelHarness.PlayButtonOf(panel, SoundProjectFixture.DoorOpen);
            ListBoxItem row = button.FindAncestorOfType<ListBoxItem>().ShouldNotBeNull();
            row.IsSelected.ShouldBeTrue();
            Fill(button).ShouldBe(Brush("SpectraTextBody"));

            Save(window, "content-list-selected@2x.png");
            window.Close();
            model.Content.ShouldNotBeNull().SetRoot(null);
        });
    }

    [Fact]
    public void The_status_bar_names_the_sound_that_plays_and_a_press_there_stops_it()
    {
        session.On(() =>
        {
            var model = new SoundPreviewModel();
            var asked = new List<string>();
            model.Send = SoundProjectFixture.Taking(asked);
            var slot = new SoundPreviewStatus { DataContext = model };
            var window = new Window
            {
                Content = new Border { Padding = new Thickness(12, 4), Child = slot },
                SizeToContent = SizeToContent.WidthAndHeight,
            };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            slot.IsEffectivelyVisible.ShouldBeFalse("nothing plays");

            model.Apply("Sounds/ambience/wind.wav");
            Dispatcher.UIThread.RunJobs();

            slot.IsEffectivelyVisible.ShouldBeTrue();
            slot.GetVisualDescendants().OfType<TextBlock>().Single().Text.ShouldBe("wind.wav");
            Save(window, "status-sound-preview@2x.png");

            ContentPanelHarness.Click(window, slot);
            asked.ShouldBe([string.Empty]);

            model.Apply(string.Empty);
            Dispatcher.UIThread.RunJobs();
            slot.IsEffectivelyVisible.ShouldBeFalse();
            window.Close();
        });
    }

    [Fact]
    public void A_sound_entitys_row_has_its_picker_and_a_play_button()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, ShellModel model) = OpenProperties(SoundProjectFixture.DoorOpen);

            SoundPreviewButton button = PlayButtons(panel).ShouldHaveSingleItem();
            button.ContentPath.ShouldBe(SoundProjectFixture.DoorOpen);
            button.ShowsStop.ShouldBeFalse();
            SoundPreviewButton.GetPreview(button).ShouldBeSameAs(model.SoundPreview);

            // The row is a field row like the number rows under it.
            Border[] rows = [.. panel.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Classes.Contains("proprow") && border.IsEffectivelyVisible)];
            Border sound = rows.Single(row => row.DataContext is PropertyRowModel { Key: "sound" });
            Border volume = rows.Single(row => row.DataContext is PropertyRowModel { Key: "volume" });
            sound.Bounds.Height.ShouldBe(volume.Bounds.Height);

            Save(window, "properties-sound@2x.png");

            model.ApplySnapshot(new SpectraEngine.Core.Hosting.FrameSnapshot
            {
                SelectedIds = [Guid.NewGuid()],
                SelectionProperties = Rows(SoundProjectFixture.DoorOpen),
                PreviewingSound = SoundProjectFixture.DoorOpen,
            });
            Dispatcher.UIThread.RunJobs();

            button.ShowsStop.ShouldBeTrue();
            Save(window, "properties-sound-playing@2x.png");

            window.Close();
        });
    }

    [Fact]
    public void A_sound_row_with_no_file_has_no_play_button()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, _) = OpenProperties(sound: null);

            PlayButtons(panel).ShouldBeEmpty();
            Save(window, "properties-sound-none@2x.png");

            window.Close();
        });
    }

    [Fact]
    public void The_rows_cell_opens_a_picker_of_the_projects_sounds()
    {
        session.On(() =>
        {
            (Window window, PropertiesPanel panel, ShellModel model) = OpenProperties(SoundProjectFixture.DoorOpen);
            model.SoundPreview.Apply(SoundProjectFixture.DoorClose);

            Button cell = panel.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("assetcell") && button.IsEffectivelyVisible);
            cell.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            AssetPickerView picker = panel.FindControl<AssetPickerView>("AssetPicker").ShouldNotBeNull();
            picker.ShownRows.Select(row => row.ContentPath).ShouldBe(["", .. SoundProjectFixture.Sounds]);
            picker.ShownRows[0].ShouldBe(AssetPickerView.NoSound);

            // The popup is not drawn inside the panel. Its buttons still find
            // the model the panel set.
            picker.GetVisualAncestors().ShouldNotContain(panel);
            SoundPreviewButton[] buttons = [.. PlayButtons(picker)];
            buttons.Length.ShouldBe(SoundProjectFixture.Sounds.Length);
            buttons.ShouldAllBe(button => ReferenceEquals(SoundPreviewButton.GetPreview(button), model.SoundPreview));
            buttons.Single(button => button.ShowsStop).ContentPath.ShouldBe(SoundProjectFixture.DoorClose);

            // The row of the file the entity has is filled with the accent,
            // and its glyph changes colour with its text.
            SoundPreviewButton chosen = buttons.Single(button => button.ContentPath == SoundProjectFixture.DoorOpen);
            chosen.FindAncestorOfType<ListBoxItem>().ShouldNotBeNull().IsSelected.ShouldBeTrue();
            Fill(chosen).ShouldBe(Brush("SpectraTextOnAccent"));
            Fill(buttons.Single(button => button.ShowsStop)).ShouldBe(Brush("SpectraMode"));

            Save(TopLevel.GetTopLevel(picker).ShouldNotBeNull(), "picker-sounds@2x.png");

            model.SoundPreview.Apply(SoundProjectFixture.DoorOpen);
            Dispatcher.UIThread.RunJobs();

            chosen.ShowsStop.ShouldBeTrue();
            Fill(chosen).ShouldBe(Brush("SpectraTextOnAccent"));
            Save(TopLevel.GetTopLevel(picker).ShouldNotBeNull(), "picker-sounds-chosen-playing@2x.png");

            window.Close();
        });
    }

    [Fact]
    public void A_picker_of_another_kind_has_no_play_buttons()
    {
        session.On(() =>
        {
            var catalog = new AssetCatalog(NullLogger.Instance);
            catalog.Rebuild(_project.Root);
            var picker = new AssetPickerView();
            var window = new Window { Content = picker, SizeToContent = SizeToContent.WidthAndHeight };
            window.Show();

            picker.Open(catalog, ContentKind.Material, string.Empty);
            Dispatcher.UIThread.RunJobs();

            picker.ShownRows.Select(row => row.Stem).ShouldBe(["None", "wall"]);
            picker.ShownRows[0].ShouldBe(AssetPickerView.None);
            PlayButtons(picker).ShouldBeEmpty();

            window.Close();
        });
    }

    private Task<(Window Window, ContentPanel Panel, ShellModel Model)> OpenContentAsync(ContentViewMode view) =>
        ContentPanelHarness.OpenAsync(_project.Root, _project.SoundsFolder, view);

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

    private (Window Window, PropertiesPanel Panel, ShellModel Model) OpenProperties(string? sound)
    {
        var panelModel = new PropertyPanelModel(_ => { }, _ => { }, _ => { })
        {
            Schemas = SoundProjectFixture.Schemas,
        };
        var model = new ShellModel { Properties = panelModel, Assets = new AssetCatalog(NullLogger.Instance) };
        model.Assets.Rebuild(_project.Root);
        panelModel.Apply(Rows(sound), 1);

        var panel = new PropertiesPanel { DataContext = model };
        var window = new Window { Content = panel, Width = 308, Height = 420 };
        window.SetRenderScaling(2.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, panel, model);
    }

    // Rows as the engine publishes them for one selected sound entity.
    private static PropertyRow[] Rows(string? sound)
    {
        var entity = new EntityData(SoundProjectFixture.ClassName);
        if (sound is not null)
            entity.SetValue("sound", sound);

        var rows = new List<PropertyRow>();
        NodeInspector.Describe(new SceneNode("DoorSound") { Entity = entity }, rows, SoundProjectFixture.Schemas);
        return [.. rows];
    }

    private static IEnumerable<SoundPreviewButton> PlayButtons(Visual within) =>
        ContentPanelHarness.PlayButtons(within);

    // The brush a button's glyph is filled with.
    private static IBrush? Fill(SoundPreviewButton button) =>
        button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Fill;

    private static IBrush? Brush(string key) =>
        Application.Current.ShouldNotBeNull().TryFindResource(key, out object? value) ? value as IBrush : null;

    // Where each visible row's name starts, from the panel's left edge.
    private static double[] NameLefts(ContentPanel panel) =>
        [.. panel.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(row => row.IsEffectivelyVisible)
            .Select(row => row.GetVisualDescendants().OfType<TextBlock>().First())
            .Select(name => RibbonProbe.BoundsIn(name, panel).X)
            .Distinct()];

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
