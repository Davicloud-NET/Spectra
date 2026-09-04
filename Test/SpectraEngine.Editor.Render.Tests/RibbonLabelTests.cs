using Avalonia.Controls;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The word on the control is the word in the roster.
/// </summary>
/// <remarks>
/// <para>
/// <b>They were two independent strings.</b> <c>RibbonItem.Label</c> and each
/// control's own <c>TextBlock</c> were written separately and nothing compared
/// them, so the roster could say "Block" while the button said something else -
/// and the ten-character cap that keeps a large label off a third line was
/// measuring the roster's string rather than the rendered one, which is the
/// half that has to fit.
/// </para>
/// <para>
/// Read off the constructed page rather than scraped, because the question is
/// what a control DISPLAYS: a chip's value half is a binding, a field displays
/// nothing at all, and a scrape has to guess which literal in the element is
/// the label.
/// </para>
/// </remarks>
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

                // A field displays a VALUE, and the steppers are two carets:
                // the roster's Label names them for the reader of the roster.
                if (item.Kind is RibbonControlKind.Field or RibbonControlKind.Stepper)
                {
                    continue;
                }

                List<string> words = control.GetVisualDescendants().OfType<TextBlock>()
                    .Select(t => t.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList()!;

                // A chip is a subject plus a bound value, so its FIRST block is
                // the label and the second is engine state.
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
        // THE CAP, MEASURED RATHER THAN COUNTED. RibbonLayoutTests holds large
        // labels to ten characters, which is a proxy: it fails first and with a
        // better message for somebody about to type one, but the fact it stands
        // in for is that the word fits the button. "Everything" broke mid-word
        // at 58 and no character count could have said so.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);

            foreach (Button button in probe.Page.GetVisualDescendants().OfType<Button>()
                         .Where(b => b.Classes.Contains("rbig")))
            {
                TextBlock label = button.GetVisualDescendants().OfType<TextBlock>().First();

                // Every line the layout produced, so a dropped character is
                // visible: MaxLines silently EATS a third line rather than
                // reporting one. Compared with >= because TextLine.Length counts
                // the line's terminator, so an exact match is off by one per
                // line - which is a thing to know rather than to rediscover.
                int laid = label.TextLayout.TextLines.Sum(l => l.Length);
                laid.ShouldBeGreaterThanOrEqualTo(
                    label.Text!.Length,
                    $"'{button.Tag}' laid out {laid} of {label.Text.Length} characters of \"{label.Text}\"");

                label.TextLayout.TextLines.Any(l => l.HasCollapsed).ShouldBeFalse(
                    $"'{button.Tag}' ellipsised \"{label.Text}\"");

                label.TextLayout.TextLines.Count.ShouldBeLessThanOrEqualTo(
                    2, $"'{button.Tag}' wraps \"{label.Text}\" past two lines");

                // THE ONE THAT ACTUALLY CATCHES IT. "Everything" broken to
                // "Everythin / g" is still two lines and still ellipsises
                // nothing, so neither check above can see it. A single word has
                // no boundary to break at, so it either fits its line or it is
                // being cut mid-word.
                if (!label.Text.Contains(' '))
                {
                    label.TextLayout.TextLines.Count.ShouldBe(
                        1, $"'{button.Tag}' breaks \"{label.Text}\" mid-word: it is one word and has nowhere to wrap");
                }
            }
        });
    }
}
