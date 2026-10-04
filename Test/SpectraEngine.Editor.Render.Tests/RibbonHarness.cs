using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// Builds the headless application the suite measures against: the shell's own
/// <see cref="App"/>, so the styles are the shipped ones.
/// </summary>
// Safe only because the headless session sets no application lifetime, so App
// builds no MainWindow and starts no engine. RibbonHarnessTests checks that.
public static class RibbonHarness
{
    /// <summary>The entry point <see cref="AvaloniaTestApplicationAttribute"/> names.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            // Must be false: the headless stub typeface gives every glyph the
            // same advance, so widths measured under it mean nothing.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            // Same embedded face and default as Program.cs. Pinning the default
            // also keeps a fallback from being whatever fontconfig returns.
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://SpectraEngine.Editor/Assets/Fonts#IBM Plex Sans",
            });
}
