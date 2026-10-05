using Avalonia.Controls;
using Avalonia.Threading;

using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// What the pointer does on the Content panel's rows and tiles: a click
/// selects, a double click activates, and a sound's play button keeps its
/// presses to itself.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class ContentPanelPointerTests(RibbonSession session) : IDisposable
{
    private readonly SoundProjectFixture _project = new();

    public void Dispose() => _project.Dispose();

    [Theory]
    [InlineData(ContentViewMode.List)]
    [InlineData(ContentViewMode.Grid)]
    public void A_double_click_on_a_file_activates_it(ContentViewMode view)
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenAsync(view);
            var activated = new List<string>();
            panel.EntryActivated += entry => activated.Add(entry.Name);

            ContentPanelHarness.Click(window, ContentPanelHarness.NameOf(panel, "door_open.wav"), times: 2);

            activated.ShouldBe(["door_open.wav"]);
            Close(window, model);
        });
    }

    [Theory]
    [InlineData(ContentViewMode.List)]
    [InlineData(ContentViewMode.Grid)]
    public void A_double_click_on_a_folder_opens_it(ContentViewMode view)
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenAsync(view);
            ContentBrowserModel browser = model.Content.ShouldNotBeNull();
            var activated = new List<string>();
            panel.EntryActivated += entry => activated.Add(entry.Name);

            ContentPanelHarness.Click(window, ContentPanelHarness.NameOf(panel, "ambience"), times: 2);

            browser.Entries.Select(entry => entry.Name).ShouldBe(["wind.wav"]);
            activated.ShouldBeEmpty("a folder is opened here and never handed to the window");
            Close(window, model);
        });
    }

    [Theory]
    [InlineData(ContentViewMode.List)]
    [InlineData(ContentViewMode.Grid)]
    public void A_click_on_a_file_selects_it(ContentViewMode view)
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenAsync(view);

            ContentPanelHarness.Click(window, ContentPanelHarness.NameOf(panel, "door_open.wav"));

            model.Content.ShouldNotBeNull().Selected.ShouldNotBeNull().Name.ShouldBe("door_open.wav");
            Close(window, model);
        });
    }

    [Theory]
    [InlineData(ContentViewMode.List)]
    [InlineData(ContentViewMode.Grid)]
    public void A_press_on_a_play_button_asks_for_the_sound_and_does_not_select_its_file(ContentViewMode view)
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenAsync(view);
            var asked = new List<string>();
            model.SoundPreview.Send = SoundProjectFixture.Taking(asked);

            ContentPanelHarness.Click(window, ContentPanelHarness.PlayButtonOf(panel, SoundProjectFixture.DoorOpen));

            asked.ShouldBe([SoundProjectFixture.DoorOpen]);
            model.Content.ShouldNotBeNull().Selected.ShouldBeNull();
            Close(window, model);
        });
    }

    [Theory]
    [InlineData(ContentViewMode.List)]
    [InlineData(ContentViewMode.Grid)]
    public void A_double_click_on_a_play_button_does_not_activate_its_file(ContentViewMode view)
    {
        session.On(async () =>
        {
            (Window window, ContentPanel panel, ShellModel model) = await OpenAsync(view);
            var asked = new List<string>();
            var activated = new List<string>();
            model.SoundPreview.Send = SoundProjectFixture.Taking(asked);
            panel.EntryActivated += entry => activated.Add(entry.Name);

            ContentPanelHarness.Click(
                window, ContentPanelHarness.PlayButtonOf(panel, SoundProjectFixture.DoorOpen), times: 2);

            asked.ShouldBe([SoundProjectFixture.DoorOpen, SoundProjectFixture.DoorOpen]);
            activated.ShouldBeEmpty();
            Close(window, model);
        });
    }

    private Task<(Window Window, ContentPanel Panel, ShellModel Model)> OpenAsync(ContentViewMode view) =>
        ContentPanelHarness.OpenAsync(_project.Root, _project.SoundsFolder, view);

    private static void Close(Window window, ShellModel model)
    {
        window.Close();
        model.Content.ShouldNotBeNull().SetRoot(null);
        Dispatcher.UIThread.RunJobs();
    }
}
