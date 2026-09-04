using Avalonia;
using Avalonia.Media;

using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The harness's own guards. Everything else in this suite is only worth its
/// numbers if these three hold.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class RibbonHarnessTests(RibbonSession session)
{
    [Fact]
    public void The_harness_starts_no_window_and_therefore_no_engine_session()
    {
        // App.OnFrameworkInitializationCompleted builds a MainWindow when a
        // classic desktop lifetime exists, and a MainWindow starts an
        // EditorSession: a graphics device, a render thread and a Win32 child
        // window, in CI. The headless session sets no lifetime, which is why
        // reusing the shell's own Application is safe - so that is a test
        // rather than a sentence in a comment.
        session.On(() =>
        {
            Application.Current.ShouldNotBeNull();
            Application.Current!.ShouldBeOfType<App>();
            Application.Current.ApplicationLifetime.ShouldBeNull();
        });
    }

    [Fact]
    public void The_embedded_face_the_type_scale_is_tuned_against_actually_resolved()
    {
        // The one silent way every other number in this suite goes wrong: the
        // embedded font fails to resolve, every label falls back to the host's
        // default face, and the widths are all subtly off with nothing failing.
        session.On(() =>
        {
            FontManager.Current
                .TryGetGlyphTypeface(new Typeface("avares://Avalonia.Fonts.Inter/Assets#Inter"), out var face)
                .ShouldBeTrue("the embedded Inter must resolve, or every measurement here is against another face");
            face.FamilyName.ShouldBe("Inter");
        });
    }

    [Theory]
    [InlineData("build")]
    [InlineData("view")]
    public void Every_page_constructs_which_is_the_roster_validator_running_in_CI(string tabId)
    {
        // Constructing a page calls ValidateAgainstRoster. Until this suite
        // existed that ran only when a human opened the window, so an id drawn
        // twice, a tagged control the roster has never heard of, a roster entry
        // with no control, or a control not wearing the class its kind requires
        // were all found on launch rather than on push.
        session.On(() =>
        {
            using var probe = RibbonProbe.Open(tabId);
            probe.Tagged().Count().ShouldBe(RibbonLayout.ItemsOf(RibbonLayout.FindTab(tabId)!).Count);
        });
    }
}
