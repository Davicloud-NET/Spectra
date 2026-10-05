using SpectraEngine.Editor.Shell.Logic;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The ruler the Logic view lays out with, in the shell's own fonts: what it
/// measures is what the graph draws.
/// </summary>
// Needs real Skia. The model's own tests measure with a ruler whose
// characters are all one width.
[Collection(RibbonSessionCollection.Name)]
public sealed class LogicTextRulerTests(RibbonSession session)
{
    [Theory]
    [InlineData("after 2 s", LogicTextStyle.Label, LogicInk.Label)]
    [InlineData("refused 999+", LogicTextStyle.Label, LogicInk.BrokenLabel)]
    [InlineData("in 111.1 s", LogicTextStyle.Label, LogicInk.LitLabel)]
    [InlineData("\"a parameter\", once", LogicTextStyle.MonoLabel, LogicInk.MonoLabel)]
    internal void A_label_given_the_room_the_ruler_measured_is_drawn_whole(string text, LogicTextStyle style, LogicInk ink)
    {
        session.On(() =>
        {
            double measured = new LogicTextRuler().Width(text, style);
            var texts = new LogicTextCache(new LogicPalette());

            // Cut short, it ends in an ellipsis and is narrower.
            measured.ShouldBeGreaterThan(0);
            texts.Get(text, ink, measured).Width.ShouldBe(measured, 0.01);
            texts.Get(text, ink, measured - 4).Width.ShouldBeLessThan(measured - 0.01);
        });
    }

    [Fact]
    public void A_running_level_keeps_room_for_the_widest_thing_a_label_may_say_in_these_fonts()
    {
        session.On(() =>
        {
            var ruler = new LogicTextRuler();
            LogicScene scene = LogicSheetTests.Drive("playing", 1123, 880).Scene.ShouldNotBeNull();

            double[] widths =
            [
                .. Enumerable.Range('0', 10)
                    .SelectMany(digit => LogicWireState.LongestTexts((char)digit))
                    .Select(text => ruler.Width(text, LogicTextStyle.Label)),
            ];

            foreach (LogicSceneEdge edge in scene.Edges)
            {
                edge.LabelBounds.ShouldNotBeNull().Width
                    .ShouldBeGreaterThanOrEqualTo(widths.Max() + 2 * LogicMetrics.LabelPadding);
            }

            LogicTheme.Missing.ShouldBeEmpty();
        });
    }

    [Fact]
    public void The_status_row_is_measured_in_its_own_size_and_a_parameter_in_mono()
    {
        session.On(() =>
        {
            var ruler = new LogicTextRuler();
            const string Text = "1 wire goes nowhere";

            ruler.Width(Text, LogicTextStyle.Status).ShouldBeGreaterThan(ruler.Width(Text, LogicTextStyle.Label));
            ruler.Width("iiii", LogicTextStyle.MonoLabel).ShouldBe(ruler.Width("mmmm", LogicTextStyle.MonoLabel), 0.01);
            ruler.Width("iiii", LogicTextStyle.Label).ShouldBeLessThan(ruler.Width("mmmm", LogicTextStyle.Label));
            ruler.Width("", LogicTextStyle.Status).ShouldBe(0);
        });
    }
}
