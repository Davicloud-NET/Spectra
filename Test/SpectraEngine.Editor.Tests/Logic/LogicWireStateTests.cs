using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Linq;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>How a wire looks and what its label says, from what it has done.</summary>
public sealed class LogicWireStateTests
{
    private const long Tick = 500;
    private const float Time = 10f;

    private static readonly LogicWireState Plain = LogicWireState.Authored(goesNowhere: false, touchesSelection: false);

    private static LogicWireState Playing(LogicWireActivity activity, bool showsRefusals = true) =>
        LogicWireState.Playing(activity, showsRefusals, Tick, Time, Plain);

    [Fact]
    public void A_wire_that_goes_nowhere_is_broken_while_editing()
    {
        LogicWireState state = LogicWireState.Authored(goesNowhere: true, touchesSelection: true);

        state.Look.ShouldBe(LogicWireLook.Broken);
        state.HasText.ShouldBeFalse();
        state.Text.ShouldBe("");
    }

    [Fact]
    public void A_wire_that_touches_a_selected_card_is_in_focus_and_the_rest_are_plain()
    {
        LogicWireState.Authored(goesNowhere: false, touchesSelection: true).Look.ShouldBe(LogicWireLook.Focus);
        Plain.Look.ShouldBe(LogicWireLook.Plain);
    }

    [Fact]
    public void A_wire_nothing_has_happened_to_stays_as_authored_while_the_level_runs()
    {
        LogicWireState focus = LogicWireState.Authored(goesNowhere: false, touchesSelection: true);

        LogicWireState.Playing(default, true, Tick, Time, focus).ShouldBe(focus);
    }

    [Fact]
    public void A_wire_that_fired_within_the_last_twenty_ticks_is_firing()
    {
        LogicWireState state = Playing(new LogicWireActivity { Fired = 3, LastFiredTick = Tick - 19 });

        state.Look.ShouldBe(LogicWireLook.Firing);
        state.Text.ShouldBe("now");
    }

    [Theory]
    [InlineData(1, "1 time")]
    [InlineData(3, "3 times")]
    [InlineData(999, "999 times")]
    [InlineData(1000, "999+ times")]
    [InlineData(250000, "999+ times")]
    public void A_wire_that_fired_earlier_says_how_often(int fired, string text)
    {
        LogicWireState state = Playing(new LogicWireActivity { Fired = fired, LastFiredTick = Tick - 20 });

        state.Look.ShouldBe(LogicWireLook.Fired);
        state.Text.ShouldBe(text);
    }

    [Fact]
    public void A_wire_with_something_queued_waits_and_says_how_long()
    {
        LogicWireState state = Playing(new LogicWireActivity
        {
            Fired = 1,
            LastFiredTick = Tick,
            Waiting = 1,
            WaitingSince = 9.4f,
            WaitingDue = 11.4f,
        });

        state.Look.ShouldBe(LogicWireLook.Waiting);
        state.Text.ShouldBe("in 1.4 s");
        state.Travel.ShouldNotBeNull().ShouldBe(0.3, 0.001);
    }

    [Fact]
    public void A_long_wait_is_capped_like_a_count()
    {
        LogicWireState state = Playing(new LogicWireActivity { Waiting = 1, WaitingSince = 0f, WaitingDue = 5000f });

        state.Text.ShouldBe("in 999+ s");
    }

    [Fact]
    public void A_wait_that_is_over_reads_as_none_left_and_the_dot_has_arrived()
    {
        LogicWireState state = Playing(new LogicWireActivity { Waiting = 1, WaitingSince = 8f, WaitingDue = 9f });

        state.Text.ShouldBe("in 0.0 s");
        state.Travel.ShouldBe(1);
    }

    [Theory]
    [InlineData(2, "missed 2")]
    [InlineData(1500, "missed 999+")]
    public void A_wire_whose_target_matched_nothing_is_broken_and_says_how_often(int missed, string text)
    {
        // A miss wins over everything else the wire did.
        LogicWireState state = Playing(new LogicWireActivity
        {
            Fired = missed,
            LastFiredTick = Tick,
            Missed = missed,
            Waiting = 1,
            WaitingDue = 20f,
        });

        state.Look.ShouldBe(LogicWireLook.Broken);
        state.IsRefusal.ShouldBeFalse();
        state.Text.ShouldBe(text);
    }

    [Fact]
    public void A_refused_wire_is_broken_on_the_edge_that_shows_refusals()
    {
        var activity = new LogicWireActivity { Fired = 4, LastFiredTick = Tick - 100, Refused = 1 };

        LogicWireState refusing = Playing(activity, showsRefusals: true);
        LogicWireState other = Playing(activity, showsRefusals: false);

        refusing.Look.ShouldBe(LogicWireLook.Broken);
        refusing.IsRefusal.ShouldBeTrue();
        refusing.Text.ShouldBe("refused 1");

        other.Look.ShouldBe(LogicWireLook.Fired);
        other.Text.ShouldBe("4 times");
    }

    [Fact]
    public void Wires_drawn_as_one_are_summed_and_the_dot_follows_the_input_due_first()
    {
        var a = new LogicWireActivity { Fired = 2, LastFiredTick = 40, Waiting = 1, WaitingSince = 1f, WaitingDue = 9f };
        var b = new LogicWireActivity { Fired = 3, LastFiredTick = 90, Missed = 1, Waiting = 2, WaitingSince = 2f, WaitingDue = 5f };

        LogicWireActivity sum = LogicWireState.Sum(a, b);

        sum.Fired.ShouldBe(5);
        sum.LastFiredTick.ShouldBe(90);
        sum.Missed.ShouldBe(1);
        sum.Waiting.ShouldBe(3);
        sum.WaitingSince.ShouldBe(2f);
        sum.WaitingDue.ShouldBe(5f);
    }

    [Fact]
    public void Nothing_a_running_wire_can_say_is_longer_than_the_texts_room_is_kept_for()
    {
        int longest = Enumerable.Range('0', 10)
            .SelectMany(digit => LogicWireState.LongestTexts((char)digit))
            .Max(text => text.Length);

        LogicWireActivity[] busy =
        [
            new() { Fired = int.MaxValue, LastFiredTick = 0 },
            new() { Fired = 1, LastFiredTick = Tick },
            new() { Missed = int.MaxValue },
            new() { Refused = int.MaxValue },
            new() { Waiting = 1, WaitingDue = 999.94f + Time, WaitingSince = 0f },
            new() { Waiting = 1, WaitingDue = float.MaxValue, WaitingSince = 0f },
        ];

        foreach (LogicWireActivity activity in busy)
            Playing(activity).Text.Length.ShouldBeLessThanOrEqualTo(longest, Playing(activity).Text);
    }

    [Fact]
    public void Two_states_that_differ_only_in_how_far_the_dot_is_say_the_same()
    {
        var early = new LogicWireState { Look = LogicWireLook.Waiting, Amount = 14, Travel = 0.2 };
        LogicWireState later = early with { Travel = 0.25 };

        later.ShouldNotBe(early);
        later.SaysTheSameAs(early).ShouldBeTrue();
        later.SaysTheSameAs(early with { Amount = 13 }).ShouldBeFalse();
    }
}
