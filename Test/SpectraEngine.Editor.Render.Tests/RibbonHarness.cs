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
/// The Avalonia application this suite measures against, and the only one:
/// it is the shell's OWN <see cref="App"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reused rather than reimplemented, and that is the whole claim this suite
/// makes.</b> A minimal test Application would have to merge Tokens, Icons and
/// DockTokens in that order, then Fluent, then Dock's theme, then
/// Controls.axaml, then set the dark variant - a verbatim copy of App.axaml,
/// which is a second declaration of one fact. The measurement's whole point is
/// "this is what the shipped app draws", and it cannot say that against a
/// parallel style graph.
/// </para>
/// <para>
/// <b>Safe because the headless session sets no application lifetime</b>, so
/// <c>App.OnFrameworkInitializationCompleted</c> never reaches the branch that
/// builds a <c>MainWindow</c> - which would start an EditorSession, a graphics
/// device, a render thread and a Win32 child window, in CI. That is a property
/// of Avalonia's session rather than of <c>App</c>, so
/// <c>RibbonHarnessTests</c> pins it rather than trusting it.
/// </para>
/// </remarks>
public static class RibbonHarness
{
    /// <summary>The entry point <see cref="AvaloniaTestApplicationAttribute"/> names.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            // FALSE IS THE WHOLE POINT. The headless render interface installs a
            // stub typeface whose every glyph has the same advance, so a width
            // measured under it is not a measurement - it is the arithmetic model
            // this suite exists to replace, wearing a framework's name.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            // Program.cs calls this too. The type scale is tuned at 11, 12 and
            // 13px against the embedded face, which is what makes a number
            // measured here mean the same thing on the machine that ships.
            .WithInterFont()
            // The last resort of BOTH font stacks. SpectraFontUi resolves to the
            // embedded Inter everywhere; SpectraFontMono names Cascadia Mono and
            // Consolas, which exist on Windows and on no Linux runner, so
            // $Default is what a Linux job actually measures the two chips in.
            // Pinning it makes that a KNOWN substitution rather than whatever
            // fontconfig hands back, and survives a container with no fonts.
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://Avalonia.Fonts.Inter/Assets#Inter",
            });
}
