using Avalonia;
using Avalonia.Media;

using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>Guards on the harness itself. The other measurements depend on these.</summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class RibbonHarnessTests(RibbonSession session)
{
    [Fact]
    public void The_harness_starts_no_window_and_therefore_no_engine_session()
    {
        // With a desktop lifetime App would build a MainWindow, which starts an
        // engine session with a graphics device and a render thread.
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
        // If the embedded font does not resolve, labels fall back to the host's
        // face and every width is slightly off with nothing failing.
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
        // Constructing a page runs ValidateAgainstRoster.
        session.On(() =>
        {
            using var probe = RibbonProbe.Open(tabId);
            probe.Tagged().Count().ShouldBe(RibbonLayout.ItemsOf(RibbonLayout.FindTab(tabId)!).Count);
        });
    }
}
