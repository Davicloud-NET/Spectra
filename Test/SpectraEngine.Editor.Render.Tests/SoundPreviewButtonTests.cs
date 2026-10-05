using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;

using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The play button beside a sound file: what it shows, what a press asks
/// for, and that a press stays with the button.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class SoundPreviewButtonTests(RibbonSession session) : IDisposable
{
    private readonly SoundProjectFixture _project = new();

    public void Dispose() => _project.Dispose();

    [Fact]
    public void The_button_shows_stop_only_while_its_file_plays()
    {
        session.On(() =>
        {
            (Window window, SoundPreviewButton button, SoundPreviewModel model) = Open(SoundProjectFixture.DoorOpen);
            button.ShowsStop.ShouldBeFalse();
            ToolTip.GetTip(button).ShouldBe(SoundPreviewButton.PlayTip);

            model.Apply(SoundProjectFixture.DoorOpen);
            button.ShowsStop.ShouldBeTrue();
            ToolTip.GetTip(button).ShouldBe(SoundPreviewButton.StopTip);

            model.Apply(SoundProjectFixture.DoorClose);
            button.ShowsStop.ShouldBeFalse();

            model.Apply(SoundProjectFixture.DoorOpen);
            model.EndSession();
            button.ShowsStop.ShouldBeFalse();

            window.Close();
        });
    }

    [Fact]
    public void A_press_asks_for_the_file_and_a_press_while_it_plays_asks_for_a_stop()
    {
        session.On(() =>
        {
            (Window window, SoundPreviewButton button, SoundPreviewModel model) = Open(SoundProjectFixture.DoorOpen);
            var asked = new List<string>();
            model.Send = SoundProjectFixture.Taking(asked);

            ContentPanelHarness.Click(window, button);
            button.ShowsStop.ShouldBeFalse("the engine has not said it plays");

            model.Apply(SoundProjectFixture.DoorOpen);
            ContentPanelHarness.Click(window, button);

            asked.ShouldBe([SoundProjectFixture.DoorOpen, string.Empty]);
            window.Close();
        });
    }

    [Fact]
    public void A_press_anywhere_in_the_buttons_box_counts_and_not_only_on_the_glyph()
    {
        session.On(() =>
        {
            (Window window, SoundPreviewButton button, SoundPreviewModel model) = Open(SoundProjectFixture.DoorOpen);
            var asked = new List<string>();
            model.Send = SoundProjectFixture.Taking(asked);
            button.Bounds.Size.ShouldBe(new Size(18, 18));

            // The glyph's ink stops short of the top edge.
            Point edge = button.TranslatePoint(new Point(9, 1), window).ShouldNotBeNull();

            window.MouseDown(edge, MouseButton.Left);
            window.MouseUp(edge, MouseButton.Left);

            asked.ShouldBe([SoundProjectFixture.DoorOpen]);
            window.Close();
        });
    }

    [Fact]
    public void A_press_that_is_dragged_off_the_button_asks_for_nothing()
    {
        session.On(() =>
        {
            (Window window, SoundPreviewButton button, SoundPreviewModel model) = Open(SoundProjectFixture.DoorOpen);
            var asked = new List<string>();
            model.Send = SoundProjectFixture.Taking(asked);
            Point centre = button.TranslatePoint(new Point(9, 9), window).ShouldNotBeNull();
            Point away = centre + new Vector(40, 0);

            window.MouseDown(centre, MouseButton.Left);
            window.MouseMove(away);
            window.MouseUp(away, MouseButton.Left);

            asked.ShouldBeEmpty();
            button.Classes.ShouldNotContain(":pressed");
            window.Close();
        });
    }

    [Fact]
    public void Another_mouse_button_asks_for_nothing()
    {
        session.On(() =>
        {
            (Window window, SoundPreviewButton button, SoundPreviewModel model) = Open(SoundProjectFixture.DoorOpen);
            var asked = new List<string>();
            model.Send = SoundProjectFixture.Taking(asked);
            Point centre = button.TranslatePoint(new Point(9, 9), window).ShouldNotBeNull();

            window.MouseDown(centre, MouseButton.Right);
            window.MouseUp(centre, MouseButton.Right);

            asked.ShouldBeEmpty();
            window.Close();
        });
    }

    [Fact]
    public void The_button_follows_the_file_it_is_given()
    {
        session.On(() =>
        {
            (Window window, SoundPreviewButton button, SoundPreviewModel model) = Open(SoundProjectFixture.DoorOpen);
            model.Apply(SoundProjectFixture.DoorClose);

            button.ContentPath = SoundProjectFixture.DoorClose;

            button.ShowsStop.ShouldBeTrue();
            window.Close();
        });
    }

    [Fact]
    public void A_button_that_is_off_screen_does_not_listen_and_catches_up_when_it_comes_back()
    {
        session.On(() =>
        {
            (Window window, SoundPreviewButton button, SoundPreviewModel model) = Open(SoundProjectFixture.DoorOpen);
            var holder = (Decorator)window.Content.ShouldNotBeNull();

            holder.Child = null;
            model.Apply(SoundProjectFixture.DoorOpen);
            button.ShowsStop.ShouldBeFalse();

            holder.Child = button;
            Dispatcher.UIThread.RunJobs();
            button.ShowsStop.ShouldBeTrue();

            window.Close();
        });
    }

    [Fact]
    public void A_hidden_button_builds_no_glyph_and_does_not_listen_until_it_is_shown()
    {
        session.On(() =>
        {
            var model = new SoundPreviewModel();
            var button = new SoundPreviewButton { ContentPath = SoundProjectFixture.DoorOpen, IsVisible = false };
            var holder = new Decorator { Child = button };
            SoundPreviewButton.SetPreview(holder, model);
            var window = new Window { Content = holder, Width = 120, Height = 80 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            model.Apply(SoundProjectFixture.DoorOpen);
            button.Child.ShouldBeNull();
            button.ShowsStop.ShouldBeFalse();

            button.IsVisible = true;

            button.Child.ShouldNotBeNull();
            button.ShowsStop.ShouldBeTrue();
            window.Close();
        });
    }

    [Fact]
    public void A_double_click_on_a_rows_play_button_does_not_pick_the_row()
    {
        session.On(() =>
        {
            (Window window, AssetPickerView picker, SoundPreviewModel model) = OpenPicker();
            var picked = new List<string>();
            var asked = new List<string>();
            picker.Picked += picked.Add;
            model.Send = SoundProjectFixture.Taking(asked);

            SoundPreviewButton button = picker.GetVisualDescendants().OfType<SoundPreviewButton>()
                .First(candidate => candidate.IsEffectivelyVisible);
            string path = button.ContentPath.ShouldNotBeNull();
            ContentPanelHarness.Click(window, button, times: 2);

            picked.ShouldBeEmpty();
            asked.ShouldBe([path, path]);

            // The same two clicks on the row's name do pick it, so the clicks are real.
            ContentPanelHarness.Click(window, ContentPanelHarness.NameOf(picker, "wind"), times: 2);

            picked.ShouldBe(["Sounds/ambience/wind.wav"]);
            window.Close();
        });
    }

    private static (Window Window, SoundPreviewButton Button, SoundPreviewModel Model) Open(string path)
    {
        var model = new SoundPreviewModel();
        var button = new SoundPreviewButton { ContentPath = path };
        var holder = new Decorator { Child = button };
        SoundPreviewButton.SetPreview(holder, model);

        var window = new Window { Content = holder, Width = 120, Height = 80 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, button, model);
    }

    private (Window Window, AssetPickerView Picker, SoundPreviewModel Model) OpenPicker()
    {
        var catalog = new AssetCatalog(NullLogger.Instance);
        catalog.Rebuild(_project.Root);

        var model = new SoundPreviewModel();
        var picker = new AssetPickerView();
        SoundPreviewButton.SetPreview(picker, model);

        var window = new Window { Content = picker, Width = 320, Height = 360 };
        window.Show();
        picker.Open(catalog, ContentKind.Sound, string.Empty);
        Dispatcher.UIThread.RunJobs();

        return (window, picker, model);
    }
}
