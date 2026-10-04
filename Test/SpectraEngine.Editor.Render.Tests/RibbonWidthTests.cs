using Avalonia;

using SpectraEngine.Editor.Shell.Ribbon;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// A ribbon page fits the window's minimum width, and the arithmetic width
/// model is an upper bound on the measured one.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class RibbonWidthTests(RibbonSession session)
{
    // The window's MinWidth. Nothing on the ribbon scrolls or collapses.
    private const double WindowMinimum = 1180.0;

    // Generous per-kind widths: the model must bound the widest control.
    private static double WidthOf(RibbonItem item) => item.Kind switch
    {
        // Widest chip is "Handles Studio".
        RibbonControlKind.Chip => 106.0,

        // TextBox.field.num at 58 plus a unit label and the stepper column.
        RibbonControlKind.Field => 100.0,

        // Stacked inside the field's row.
        RibbonControlKind.Stepper => 0.0,

        // 112 bounds the widest small row, "Light panel".
        _ => item.Size == RibbonItemSize.Large ? 64.0 : 112.0,
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
        // Measured in the widest state: the chips show bound values, and
        // "Classic", "local" and "deg" are the longer ones.
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
        // The model ignores group captions. RibbonGeometryTests checks that a
        // caption never widens its group.
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

    private static void Widest(SpectraEngine.Editor.Shell.ShellModel model)
    {
        model.RequestGizmoStyle("Classic");
        model.RequestOrientation("local");
        model.RequestGizmoMode("rotate");
    }

    // Page width from the roster alone, with no layout pass.
    private static double Modelled(RibbonTab tab)
    {
        double width = 0;

        foreach (RibbonGroup group in tab.Groups)
        {
            double large = group.Items.Where(i => i.Size == RibbonItemSize.Large).Sum(WidthOf);

            // Small items stack three to a column.
            List<RibbonItem> small = group.Items
                .Where(i => i.Size != RibbonItemSize.Large && WidthOf(i) > 0)
                .ToList();

            double columns = 0;
            for (int i = 0; i < small.Count; i += 3)
            {
                columns += small.Skip(i).Take(3).Max(WidthOf);
            }

            width += large + columns + 10; // StackPanel.ribbongroup's margin
        }

        return width + ((tab.Groups.Count - 1) * 5); // the rules between groups
    }
}
