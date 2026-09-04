using Avalonia;

using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// A page has to fit the window the shell refuses to go below, and the cheap
/// arithmetic bound that stands in for that has to actually bound it.
/// </summary>
/// <remarks>
/// <para>
/// The model moved here from <c>RibbonLayoutTests</c> so it sits beside the
/// measurement rather than in a project that cannot take one. It stays, because
/// it fails on a roster edit alone with no window and no fonts, and its message
/// names the page. What it could never do is be checked: it invents a width per
/// control kind and calls itself "deliberately generous", and nothing tested
/// that claim.
/// </para>
/// <para>
/// <b>Neither replaces the other.</b> The measurement is the truth and the model
/// is the fast bound; the second test below is what makes the model's own
/// premise a fact.
/// </para>
/// </remarks>
[Collection(RibbonSessionCollection.Name)]
public sealed class RibbonWidthTests(RibbonSession session)
{
    /// <summary>
    /// The window's own <c>MinWidth</c>. A page has to fit inside it, because
    /// nothing here scrolls or collapses a group.
    /// </summary>
    private const double WindowMinimum = 1180.0;

    /// <summary>What each control kind measures, from the theme's own numbers.</summary>
    private static double WidthOf(RibbonItem item) => item.Kind switch
    {
        // Button.chip.compact plus its two words; the widest is "Handles Studio".
        RibbonControlKind.Chip => 106.0,

        // TextBox.field.num at 58 plus a unit label and the stepper column.
        RibbonControlKind.Field => 100.0,

        // Both steppers stack inside the field's own row.
        RibbonControlKind.Stepper => 0.0,

        _ => item.Size == RibbonItemSize.Large ? 64.0 : 96.0,
    };

    [Theory]
    [InlineData("build", 1.0)]
    [InlineData("view", 1.0)]
    [InlineData("build", 1.25)]
    [InlineData("view", 1.25)]
    [InlineData("build", 1.5)]
    [InlineData("view", 1.5)]
    public void A_page_fits_the_window_the_shell_refuses_to_go_below(string tabId, double scaling)
    {
        // MEASURED, at the three scalings Tokens.axaml claims to stack cleanly
        // at and nothing checked. The model below is a bound; this is the width.
        //
        // The model is driven to the page's WIDEST reachable state through the
        // same public API MainWindow uses - "Classic" is a character longer than
        // "Studio", "local" than "world", "deg" than "su" - because the two chips
        // render bound values and a page measured with a null DataContext is a
        // page that never exists on screen.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId, scaling, Widest);

            double measured = probe.Body.DesiredSize.Width;
            measured.ShouldBeLessThan(
                WindowMinimum,
                $"the '{tabId}' page measures {measured:0.#} against the window's MinWidth of {WindowMinimum}");
        });
    }

    [Theory]
    [MemberData(nameof(RibbonGeometryTests.Pages), MemberType = typeof(RibbonGeometryTests))]
    public void The_arithmetic_bound_is_actually_a_bound(string tabId)
    {
        // The model's own comment says it is "deliberately generous per item:
        // the point is a bound that fails before the layout does, not a
        // measurement of it". That was an assertion about itself. This is the
        // test of it, and it is the same move RibbonDepthConventionTests already
        // makes when one of its three claims is what makes the other two cheap.
        //
        // The model ignores group captions entirely, which is legal only because
        // RibbonGeometryTests holds that a caption never widens its group.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId, drive: Widest);

            double modelled = Modelled(RibbonLayout.FindTab(tabId)!);
            double measured = probe.Page.DesiredSize.Width;

            modelled.ShouldBeGreaterThanOrEqualTo(
                measured,
                $"the '{tabId}' page models {modelled:0.#} and measures {measured:0.#}; " +
                "the model has stopped being a bound");
        });
    }

    /// <summary>The page's widest reachable state, through the shell's own API.</summary>
    private static void Widest(SpectraEngine.Editor.Shell.ShellModel model)
    {
        model.RequestGizmoStyle("Classic");
        model.RequestOrientation("local");
        model.RequestGizmoMode("rotate");
    }

    /// <summary>What the roster says a page costs, before anything is laid out.</summary>
    private static double Modelled(RibbonTab tab)
    {
        double width = 0;

        foreach (RibbonGroup group in tab.Groups)
        {
            double large = group.Items.Where(i => i.Size == RibbonItemSize.Large).Sum(WidthOf);

            // Everything that is not a large button lives in a column, and a
            // column holds three rows.
            List<RibbonItem> small = group.Items
                .Where(i => i.Size != RibbonItemSize.Large && WidthOf(i) > 0)
                .ToList();

            double columns = 0;
            for (int i = 0; i < small.Count; i += 3)
            {
                columns += small.Skip(i).Take(3).Max(WidthOf);
            }

            width += large + columns + 10; // StackPanel.ribbongroup's own margin
        }

        return width + ((tab.Groups.Count - 1) * 5); // the rules between them
    }
}
