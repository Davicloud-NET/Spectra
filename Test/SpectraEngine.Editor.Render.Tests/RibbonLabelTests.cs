using Avalonia.Controls;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>The word on each ribbon control is the roster's label, and it fits.</summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class RibbonLabelTests(RibbonSession session)
{
    [Theory]
    [MemberData(nameof(RibbonGeometryTests.Pages), MemberType = typeof(RibbonGeometryTests))]
    public void Every_control_that_shows_a_word_shows_the_one_the_roster_names(string tabId)
    {
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId, drive: m => m.SetEntityClasses(null));
            int checkedAny = 0;

            foreach (Control control in probe.Tagged())
            {
                string id = (string)control.Tag!;
                RibbonItem item = RibbonLayout.FindItem(id)!;

                // A field shows a value and a stepper shows two carets, not the label.
                if (item.Kind is RibbonControlKind.Field or RibbonControlKind.Stepper)
                {
                    continue;
                }

                List<string> words = control.GetVisualDescendants().OfType<TextBlock>()
                    .Select(t => t.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList()!;

                // A chip's first block is the label, the second a bound value.
                string shown = words.Count > 0 ? words[0] : string.Empty;

                shown.ShouldBe(item.Label, $"'{id}' draws its own word independently of the roster's");
                checkedAny++;
            }

            checkedAny.ShouldBeGreaterThan(4, "the page's labelled controls should have been read");
        });
    }

    [Theory]
    [MemberData(nameof(RibbonGeometryTests.Pages), MemberType = typeof(RibbonGeometryTests))]
    public void A_large_label_really_does_fit_on_two_lines(string tabId)
    {
        // RibbonLayoutTests caps large labels at ten characters. That is a
        // proxy; this measures whether the word fits.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);

            foreach (Button button in probe.Page.GetVisualDescendants().OfType<Button>()
                         .Where(b => b.Classes.Contains("rbig")))
            {
                TextBlock label = button.GetVisualDescendants().OfType<TextBlock>().First();

                // MaxLines drops a third line without reporting it. >= because
                // TextLine.Length counts the line terminator.
                int laid = label.TextLayout.TextLines.Sum(l => l.Length);
                laid.ShouldBeGreaterThanOrEqualTo(
                    label.Text!.Length,
                    $"'{button.Tag}' laid out {laid} of {label.Text.Length} characters of \"{label.Text}\"");

                label.TextLayout.TextLines.Any(l => l.HasCollapsed).ShouldBeFalse(
                    $"'{button.Tag}' ellipsised \"{label.Text}\"");

                label.TextLayout.TextLines.Count.ShouldBeLessThanOrEqualTo(
                    2, $"'{button.Tag}' wraps \"{label.Text}\" past two lines");

                // A single word cut mid-word is still two lines with no
                // ellipsis, so the checks above miss it.
                if (!label.Text.Contains(' '))
                {
                    label.TextLayout.TextLines.Count.ShouldBe(
                        1, $"'{button.Tag}' breaks \"{label.Text}\" mid-word: it is one word and has nowhere to wrap");
                }
            }
        });
    }
}
