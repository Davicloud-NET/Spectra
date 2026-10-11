using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SpectraEngine.Editor.Render.Tests;

// What the sheets that show scripts share: writing a frame where a person
// looks for it, and finding the controls that are on screen.
internal static class ScriptSheet
{
    // Two ticks: a change made while the last one waits to be drawn is only
    // handed to the renderer once that one has been.
    public static void Save(TopLevel surface, string fileName)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        WriteableBitmap frame = surface.GetLastRenderedFrame().ShouldNotBeNull("nothing was rasterised");

        Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
        frame.Save(Path.Combine(RibbonSheetTests.OutputDirectory, fileName), new PngBitmapEncoderOptions());
    }

    public static T[] Shown<T>(Visual within) where T : Control =>
        [.. within.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible)];
}
