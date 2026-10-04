using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

using SpectraEngine.Editor.Shell.Ribbon;

using AvPath = Avalonia.Controls.Shapes.Path;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// Ribbon geometry on a real layout pass with real text metrics.
/// Nothing here judges colour or contrast.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class RibbonGeometryTests(RibbonSession session)
{
    // Large glyphs fill 3.5..28.5 of a 32 box, plus the 1.6 stroke.
    private const double NominalLargeInk = 26.6;

    private const double LargeInkTolerance = 4.0;

    /// <summary>Both ribbon pages, by roster id.</summary>
    public static TheoryData<string> Pages => [RibbonLayout.DefaultTabId, RibbonLayout.ViewTabId];

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_glyph_is_centred_in_the_box_layout_gave_it(string tabId)
    {
        // A Shape with Stretch="None" draws at its authored coordinates and
        // defaults to HorizontalAlignment=Stretch, so a well-placed box can hold
        // ink stuck to its left edge. Layout bounds cannot see that; ink can.
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

                // Tolerance scales with the slack, not a pixel count: edge-drawn
                // ink is off by half the room it had. The artwork itself is off
                // centre by up to ~1px, which must pass.
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
        // The tolerance is loose because the shipped set is uneven: most glyphs
        // are 26.6 square, but Part is 30.3 tall, Light 24.6 and Cut 25.1.
        // Tighten to about a pixel once the artwork is normalised.
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
        // Centred or top-aligned is the group's choice, but its columns must
        // agree. That is what StackPanel.rcol.ragged is for.
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
        // StackPanel.rrow's MinHeight does this: the Snap group is shorter
        // than a row of large buttons.
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
        // The arithmetic width model ignores captions. This is the
        // precondition that lets it.
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
        // Roster controls only. Parts overlap on purpose: Border.sheen under
        // large buttons, Path.mark over Path.markbox.
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

    // Half of what edge-drawn ink would be off by, with a floor for the artwork.
    private static double Allowed(double boxSize, double inkSize) =>
        Math.Max(1.2, Math.Max(0, boxSize - inkSize) / 4);

    private static IEnumerable<StackPanel> Groups(RibbonProbe probe) =>
        probe.Page.GetVisualDescendants().OfType<StackPanel>().Where(s => s.Classes.Contains("ribbongroup"));

    private static TextBlock? CaptionBlock(StackPanel group) =>
        group.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Classes.Contains("ribbongroupcaption"));

    private static string Caption(StackPanel group) => CaptionBlock(group)?.Text ?? "?";
}
