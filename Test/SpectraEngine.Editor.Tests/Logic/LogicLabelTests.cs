using SpectraEngine.Editor.Shell.Logic;
using System.Globalization;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>The words a wire carries: its parameter, its delay and how often it may fire.</summary>
public sealed class LogicLabelTests
{
    [Fact]
    public void A_wire_with_nothing_set_has_no_label()
    {
        LogicLabel.Of(Wire("OnPressed", "Door", "Open")).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_parameter_is_quoted_and_drawn_in_mono()
    {
        LogicLabel.Of(Wire("OnPressed", "Counter", "Add", "1")).ShouldBe(new LogicLabel("\"1\"", true));
    }

    [Theory]
    [InlineData(1, "once")]
    [InlineData(3, "3 times")]
    [InlineData(0, "")]
    [InlineData(-1, "")]
    [InlineData(-7, "")]
    public void A_firing_limit_reads_as_once_or_as_a_count(int times, string expected)
    {
        LogicLabel.Of(Wire("OnPressed", "Door", "Open", times: times)).Text.ShouldBe(expected);
    }

    [Theory]
    [InlineData(2f, "after 2 s")]
    [InlineData(1.5f, "after 1.5 s")]
    [InlineData(0.25f, "after 0.25 s")]
    [InlineData(12.1f, "after 12.1 s")]
    [InlineData(0f, "")]
    public void A_delay_is_written_without_trailing_zeros(float delay, string expected)
    {
        LogicLabel.Of(Wire("OnPressed", "Door", "Open", delay: delay)).Text.ShouldBe(expected);
    }

    [Fact]
    public void A_delay_reads_the_same_under_a_comma_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            LogicLabel.Of(Wire("OnPressed", "Door", "Open", delay: 1.5f)).Text.ShouldBe("after 1.5 s");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_parts_join_as_parameter_then_delay_then_times()
    {
        LogicLabel label = LogicLabel.Of(Wire("OnPressed", "Counter", "Add", "5", delay: 2f, times: 3));

        label.Text.ShouldBe("\"5\", after 2 s, 3 times");
        label.IsMono.ShouldBeTrue();
    }

    [Fact]
    public void The_vault_levels_wires_carry_what_they_were_authored_with()
    {
        LogicGraph graph = Vault();

        graph.Edges.Select(edge => $"{edge.From.Name} > {edge.To.Name}: {edge.Label.Text}").ShouldBe(
        [
            "ButtonA > Presses: \"1\"",
            "ButtonB > Presses: \"1\"",
            "Presses > OpenVault: ",
            "OpenVault > VaultDoor: once",
            "OpenVault > Lift: after 2 s",
            "OpenVault > VaultDor: ",
            "Lift > Lift: after 3 s",
            "LiftButton > Lift: ",
            "StartZone > StartDoor: ",
        ]);
    }

    [Fact]
    public void A_caller_can_put_other_words_on_an_edge()
    {
        LogicEdge edge = Vault().Edge("OpenVault", "Lift");
        LogicEdge relabelled = edge.WithLabel(new LogicLabel("in 1.4 s", false));

        relabelled.Label.Text.ShouldBe("in 1.4 s");
        relabelled.Wire.ShouldBe(edge.Wire);
        relabelled.To.ShouldBeSameAs(edge.To);
        edge.Label.Text.ShouldBe("after 2 s");
    }
}
