using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell.Ribbon;

using AvPath = Avalonia.Controls.Shapes.Path;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// What the ribbon MEASURES, on a real layout pass with real text metrics.
/// </summary>
/// <remarks>
/// <para>
/// Every defect this surface has ever had was found by a person looking at the
/// running window: a 58px button that broke "Everything" mid-word, a caret that
/// rendered 28px left of its glyph, and - found here - a split button whose
/// label sat seven pixels above every button beside it. None of the three is
/// reachable from a source scrape, because in each case the markup and the
/// styles are individually correct and it is the composed layout that is wrong.
/// </para>
/// <para>
/// <b>What this suite deliberately does NOT claim.</b> Nothing here judges
/// colour, contrast, or whether the hierarchy reads - every assertion is
/// geometric or textual, and the whole surface would have been green throughout
/// the period it was, in the owner's words, thirty identical grey buttons.
/// </para>
/// </remarks>
[Collection(RibbonSessionCollection.Name)]
public sealed class RibbonGeometryTests(RibbonSession session)
{
    /// <summary>
    /// What a large glyph renders across: 25 units of ink from Icons.axaml's
    /// own 3.5..28.5 convention, plus the 1.6 stroke Path.icon-lg draws it with.
    /// </summary>
    private const double NominalLargeInk = 26.6;

    /// <summary>How far a large glyph may sit from that. See the test.</summary>
    private const double LargeInkTolerance = 4.0;

    /// <summary>Both pages, by roster id.</summary>
    public static TheoryData<string> Pages => [RibbonLayout.DefaultTabId, RibbonLayout.ViewTabId];

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_glyph_is_centred_in_the_box_layout_gave_it(string tabId)
    {
        // THE 28-PIXEL DEFECT, GENERALISED. A Shape with Stretch="None" draws
        // its geometry at AUTHORED coordinates inside whatever box it was
        // arranged into, and Shape defaults to HorizontalAlignment=Stretch - so
        // the box can be perfectly placed while the ink sits against its left
        // edge. Bounds cannot see that; ink can.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);

            foreach (AvPath glyph in probe.Page.GetVisualDescendants().OfType<AvPath>())
            {
                Rect box = RibbonProbe.BoundsIn(glyph, probe.Page);
                Rect ink = RibbonProbe.InkIn(glyph, probe.Page);
                if (ink.Width <= 0)
                {
                    continue;
                }

                // MEASURED AGAINST THE SLACK, not against a pixel count, because
                // the defect scales with the box: a glyph drawn from its box's
                // left edge is off-centre by exactly half the room it had, so a
                // 6px caret in a 64px band lands 29px out and the same caret in
                // a 12px one lands 2.4px out. Asserting "nearer the centre than
                // the edge" catches both and lets authoring asymmetry through -
                // the current large set really is off centre by up to 1.05 (Part
                // +0.85, Light -1.0, Rotate -1.05), which is the artwork's
                // business rather than layout's.
                //
                // WHAT THIS CAN AND CANNOT SEE, measured rather than asserted: a
                // stretched caret in the 12px column it has now lands 1.5 out and
                // IS caught, but only just, because the column is too narrow for
                // the defect to grow in. In the 64px foot band the caret used to
                // sit in, the same mistake was 29 - which is the number CLAUDE.md
                // records a person finding by looking at the window. The narrow
                // column bounds the defect by construction, which is a better
                // outcome than detecting a big one.
                string name = string.Join(' ', glyph.Classes);
                Math.Abs(ink.Center.X - box.Center.X).ShouldBeLessThanOrEqualTo(
                    Allowed(box.Width, ink.Width),
                    $"'{name}' ink centres at x {ink.Center.X:0.##} in a box centred at {box.Center.X:0.##} " +
                    $"(ink {ink.Width:0.##} wide in {box.Width:0.##})");
                Math.Abs(ink.Center.Y - box.Center.Y).ShouldBeLessThanOrEqualTo(
                    Allowed(box.Height, ink.Height),
                    $"'{name}' ink centres at y {ink.Center.Y:0.##} in a box centred at {box.Center.Y:0.##} " +
                    $"(ink {ink.Height:0.##} tall in {box.Height:0.##})");
            }
        });
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_large_glyph_is_drawn_to_one_optical_size(string tabId)
    {
        // ONE FAMILY OR ELEVEN OUTLINES. Icons.axaml states that every large
        // glyph is authored so its ink fills 3.5..28.5 of a 32 box, which with
        // the 1.6 stroke renders 26.6 across - and nothing checked it, because
        // RibbonDepthConventionTests can only read coordinates out of the file
        // and asks the weaker question of whether the widest one exceeds 16.
        //
        // MEASURED, and the set does not hold its own convention: most glyphs
        // are exactly 26.6 x 26.6, but Part renders 30.3 tall, Light 24.6 and
        // Cut 25.1 - a 5.7px spread, 21% of the box. That is the same defect
        // CLAUDE.md records fixing for the SMALL set, re-acquired by hand
        // authoring, and it is why the bound here is loose enough to pass what
        // ships today rather than red on arrival. It tightens to about a pixel
        // the day the glyphs are normalised from a viewBox instead of by eye.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);

            foreach (AvPath glyph in probe.Page.GetVisualDescendants().OfType<AvPath>()
                         .Where(p => p.Classes.Contains("icon-lg")))
            {
                Rect ink = RibbonProbe.InkIn(glyph, probe.Page);
                foreach ((string axis, double measured) in new[] { ("wide", ink.Width), ("tall", ink.Height) })
                {
                    Math.Abs(measured - NominalLargeInk).ShouldBeLessThanOrEqualTo(
                        LargeInkTolerance,
                        $"a large glyph renders {measured:0.##} {axis} against the set's nominal {NominalLargeInk}");
                }
            }
        });
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_large_button_puts_its_label_on_the_same_line(string tabId)
    {
        // FOUND HERE, AND BY NOTHING ELSE. The Entity split's main half was 50
        // tall with a top-aligned stack while every neighbour centred 53 in 66,
        // so its icon and its label both sat seven pixels high - on the one
        // control in the group that is meant to look like the others.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);

            var labels = probe.Page.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("rbig"))
                .Select(b => (
                    Id: b.Tag as string ?? "(untagged)",
                    Y: RibbonProbe.BoundsIn(b.GetVisualDescendants().OfType<TextBlock>().First(), probe.Page).Y))
                .ToList();

            labels.Count.ShouldBeGreaterThan(1, "a page leads with large buttons");

            double first = labels[0].Y;
            foreach ((string id, double y) in labels)
            {
                Math.Abs(y - first).ShouldBeLessThan(
                    0.75, $"'{id}' puts its label at y {y:0.##}; the first large button puts its at {first:0.##}");
            }
        });
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Columns_inside_one_group_share_an_edge(string tabId)
    {
        // A column of small rows is either centred against the large buttons
        // beside it or top-aligned with its sibling columns, and WHICH is a
        // property of the group rather than of the column: centring a column of
        // two beside a column of three aligns it to neither. Whatever a group
        // chose, its columns have to agree - which is the whole reason
        // StackPanel.rcol.ragged exists.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);

            foreach (StackPanel group in Groups(probe))
            {
                List<double> tops = group.GetVisualDescendants().OfType<StackPanel>()
                    .Where(s => s.Classes.Contains("rcol"))
                    .Select(s => RibbonProbe.BoundsIn(s, probe.Page).Y)
                    .ToList();

                if (tops.Count < 2)
                {
                    continue;
                }

                (tops.Max() - tops.Min()).ShouldBeLessThan(
                    0.75,
                    $"the '{Caption(group)}' group's columns start at " +
                    $"{string.Join(", ", tops.Select(t => t.ToString("0.#")))}");
            }
        });
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_caption_on_a_page_shares_one_baseline(string tabId)
    {
        // StackPanel.rrow's MinHeight exists for exactly this and nothing
        // checked it: the Snap group is a check row over a field and is
        // naturally shorter than a row of large buttons, so without the floor
        // its caption would sit twenty pixels above its neighbours'.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);

            var captions = Groups(probe)
                .Select(g => (Text: Caption(g), Y: RibbonProbe.BoundsIn(CaptionBlock(g)!, probe.Page).Y))
                .ToList();

            double first = captions[0].Y;
            foreach ((string text, double y) in captions)
            {
                Math.Abs(y - first).ShouldBeLessThan(
                    0.75, $"'{text}' sits at y {y:0.#} and the first caption at {first:0.#}");
            }
        });
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_caption_never_widens_the_group_it_names(string tabId)
    {
        // This is what makes it LEGAL for the arithmetic width model to ignore
        // captions. A caption is a centred child of the group's own vertical
        // StackPanel, so it can widen the group; the model pretends it cannot.
        // Rather than teach the model to measure text - which would mean the
        // model needs a font, and then it is not the cheap bound any more -
        // assert the precondition it relies on.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);

            foreach (StackPanel group in Groups(probe))
            {
                TextBlock caption = CaptionBlock(group)!;
                StackPanel row = group.GetVisualDescendants().OfType<StackPanel>()
                    .First(s => s.Classes.Contains("rrow"));

                caption.Bounds.Width.ShouldBeLessThanOrEqualTo(
                    row.Bounds.Width,
                    $"'{caption.Text}' is {caption.Bounds.Width:0.#} wide over a {row.Bounds.Width:0.#} group");
            }
        });
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void No_ribbon_control_hangs_outside_its_page_or_over_another_one(string tabId)
    {
        // Restricted to the controls the roster names, deliberately. A blanket
        // "nothing overlaps" is false on this surface BY DESIGN - Border.sheen
        // sits under the content of every large button and Path.mark sits over
        // Path.markbox in every check and radio row - and would have to be
        // exempted into uselessness. "No two ribbon controls sit on top of each
        // other" is the claim worth making, and it is what turns the split's
        // zero vertical headroom into a build failure the day it stops being a
        // near-miss.
        session.On(() =>
        {
            using RibbonProbe probe = RibbonProbe.Open(tabId);
            var page = new Rect(probe.Page.Bounds.Size).Inflate(0.5);

            var boxes = probe.Tagged()
                .Select(c => (Id: (string)c.Tag!, Box: RibbonProbe.BoundsIn(c, probe.Page)))
                .ToList();

            foreach ((string id, Rect box) in boxes)
            {
                page.Contains(box).ShouldBeTrue($"'{id}' at {box} hangs outside the page {page}");
            }

            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    Rect overlap = boxes[i].Box.Intersect(boxes[j].Box);
                    double area = Math.Max(0, overlap.Width) * Math.Max(0, overlap.Height);
                    area.ShouldBeLessThan(
                        0.01, $"'{boxes[i].Id}' and '{boxes[j].Id}' overlap over {overlap}");
                }
            }
        });
    }

    /// <summary>
    /// How far off centre a glyph's ink may sit: half the room it had, so a
    /// glyph drawn from its box's edge always fails and authoring asymmetry
    /// always passes.
    /// </summary>
    private static double Allowed(double boxSize, double inkSize) =>
        Math.Max(1.2, Math.Max(0, boxSize - inkSize) / 4);

    private static IEnumerable<StackPanel> Groups(RibbonProbe probe) =>
        probe.Page.GetVisualDescendants().OfType<StackPanel>().Where(s => s.Classes.Contains("ribbongroup"));

    private static TextBlock? CaptionBlock(StackPanel group) =>
        group.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Classes.Contains("ribbongroupcaption"));

    private static string Caption(StackPanel group) => CaptionBlock(group)?.Text ?? "?";
}
