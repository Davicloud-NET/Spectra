using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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
            ToolTip.GetTip(Inner(button)).ShouldBe(SoundPreviewButton.PlayTip);

            model.Apply(SoundProjectFixture.DoorOpen);
            button.ShowsStop.ShouldBeTrue();
            ToolTip.GetTip(Inner(button)).ShouldBe(SoundPreviewButton.StopTip);

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
            model.Requested += asked.Add;

            Inner(button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.ShowsStop.ShouldBeFalse("the engine has not said it plays");

            model.Apply(SoundProjectFixture.DoorOpen);
            Inner(button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            asked.ShouldBe([SoundProjectFixture.DoorOpen, string.Empty]);
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
    public void A_double_click_on_a_rows_play_button_does_not_pick_the_row()
    {
        session.On(() =>
        {
            (Window window, AssetPickerView picker, SoundPreviewModel model) = OpenPicker();
            var picked = new List<string>();
            var asked = new List<string>();
            picker.Picked += picked.Add;
            model.Requested += asked.Add;

            SoundPreviewButton button = picker.GetVisualDescendants().OfType<SoundPreviewButton>()
                .First(candidate => candidate.IsEffectivelyVisible);
            string path = button.ContentPath.ShouldNotBeNull();
            DoubleClick(window, button);

            picked.ShouldBeEmpty();
            asked.ShouldBe([path, path]);

            // The same two clicks on the row's name do pick it, so the clicks are real.
            TextBlock name = picker.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text == "wind" && text.IsEffectivelyVisible);
            DoubleClick(window, name);

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

    private static Button Inner(SoundPreviewButton button) =>
        button.GetVisualDescendants().OfType<Button>().Single();

    private static void DoubleClick(Window window, Visual target)
    {
        Point centre = target.TranslatePoint(
            new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window).ShouldNotBeNull();

        for (int click = 0; click < 2; click++)
        {
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
        }

        Dispatcher.UIThread.RunJobs();
    }
}
