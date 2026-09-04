using Avalonia.Controls;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The ribbon refuses the keyboard, and the snap field is the one exception.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this closes was severe and completely silent.</b> W, E, R, 2,
/// 3, 4, X, Y, G, the bracket pair, Escape, Ctrl+D, Delete, Ctrl+G,
/// Ctrl+Shift+G and Ctrl+T all run through the ENGINE keymap, which fires only
/// while the viewport holds the keyboard - they are not window key bindings. So
/// one click anywhere on the ribbon killed every one of them until the user
/// clicked back in the scene, while the ribbon's own tooltips went on
/// advertising them.
/// </para>
/// <para>
/// Asserted on constructed controls rather than scraped out of the styles,
/// because the claim is about what a control ENDS UP with: a setter can be
/// present and beaten by a more specific style, and Avalonia reports nothing
/// when it is.
/// </para>
/// </remarks>
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
                    // THE ONE EXCEPTION, and it is the whole contract of the
                    // control: GotFocus selects all, LostFocus commits,
                    // RefreshSnapField stands down while it is focused, and
                    // Escape reverts and hands the keyboard back.
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
        // The second half of the same defect, and the nastier one: a focused
        // Avalonia Button re-invokes on Space and Enter, so pressing Space after
        // clicking Delete deleted a second node. Nothing about that is visible
        // in the markup.
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
