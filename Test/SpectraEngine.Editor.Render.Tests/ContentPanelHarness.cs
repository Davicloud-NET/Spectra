using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;

using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

// A Content panel over a folder on disk, in a window, and the pointer a test
// drives it with.
internal static class ContentPanelHarness
{
    // Call on the session's UI thread. The window is 2x, as the sheets are.
    public static async Task<(Window Window, ContentPanel Panel, ShellModel Model)> OpenAsync(
        string root, string folder, ContentViewMode view)
    {
        var browser = new ContentBrowserModel(NullLogger.Instance) { ViewMode = view };
        var model = new ShellModel { Content = browser };

        browser.SetRoot(root);
        await browser.Index.Walking;
        browser.NavigateTo(folder);

        var panel = new ContentPanel { DataContext = model };
        var window = new Window { Content = panel, Width = 760, Height = 300 };
        window.SetRenderScaling(2.0);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, panel, model);
    }

    public static IEnumerable<SoundPreviewButton> PlayButtons(Visual within) =>
        within.GetVisualDescendants().OfType<SoundPreviewButton>().Where(button => button.IsEffectivelyVisible);

    // The play button of the row or tile that shows one file.
    public static SoundPreviewButton PlayButtonOf(Visual within, string contentPath) =>
        PlayButtons(within).Single(button => button.ContentPath == contentPath);

    // The text that shows a file's name in a row or on a tile.
    public static TextBlock NameOf(Visual within, string name) =>
        within.GetVisualDescendants().OfType<TextBlock>()
            .First(text => text.Text == name && text.IsEffectivelyVisible);

    public static void Click(Window window, Visual target, int times = 1)
    {
        Point centre = target.TranslatePoint(
            new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window).ShouldNotBeNull();

        for (int click = 0; click < times; click++)
        {
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
        }

        Dispatcher.UIThread.RunJobs();
    }
}
