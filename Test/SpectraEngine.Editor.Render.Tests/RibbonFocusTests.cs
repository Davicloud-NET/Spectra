using Avalonia.Controls;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>The ribbon refuses the keyboard. The snap field is the one exception.</summary>
// Tool keys go through the engine keymap, which only fires while the viewport
// has the keyboard. A focusable ribbon control would take them away on a click.
// Checked on constructed controls: a Focusable setter can lose to another style.
[Collection(RibbonSessionCollection.Name)]
public sealed class RibbonFocusTests(RibbonSession session)
{
    [Theory]
    [MemberData(nameof(RibbonGeometryTests.Pages), MemberType = typeof(RibbonGeometryTests))]
    public void No_ribbon_control_takes_the_keyboard_except_the_field(string tabId)
    {
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);

            foreach (Control control in probe.Tagged())
            {
                string id = (string)control.Tag!;
                RibbonItem item = RibbonLayout.FindItem(id)!;

                if (item.Kind == RibbonControlKind.Field)
                {
                    control.Focusable.ShouldBeTrue($"'{id}' is a field and is typed into");
                    continue;
                }

                control.Focusable.ShouldBeFalse(
                    $"'{id}' can take the keyboard, which kills every engine-keymap chord " +
                    "until the user clicks back in the viewport");
            }
        });
    }

    [Theory]
    [MemberData(nameof(RibbonGeometryTests.Pages), MemberType = typeof(RibbonGeometryTests))]
    public void A_button_that_cannot_be_focused_cannot_be_re_invoked_by_Space(string tabId)
    {
        // A focused Avalonia Button re-invokes on Space and Enter, so Space
        // after clicking Delete would delete again.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);

            IEnumerable<Button> buttons = probe.Page.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Tag is string id && RibbonLayout.FindItem(id) is { Kind: not RibbonControlKind.Field });

            foreach (Button button in buttons)
            {
                button.Focusable.ShouldBeFalse($"'{button.Tag}' would answer Space after a click");
            }
        });
    }
}
