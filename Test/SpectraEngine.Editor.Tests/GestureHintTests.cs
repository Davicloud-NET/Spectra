using SpectraEngine.Core.Hosting;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// What the status bar says the mouse does right now.
/// </summary>
/// <remarks>
/// <b>The editor had no contextual disclosure at all</b>, and spent the space on
/// five engine counters instead. Continuous hints are the only teaching
/// mechanism that reaches people who never read documentation, which is nearly
/// everybody: they start clicking and learn by doing.
/// </remarks>
public sealed class GestureHintTests
{
    [Theory]
    [InlineData("look", "fly")]
    [InlineData("orbit", "orbit")]
    [InlineData("pan", "pan")]
    [InlineData("drag-box", "box select")]
    [InlineData("hover-object", "click selects")]
    [InlineData("hover-empty", "box-selects")]
    public void Each_state_says_what_that_state_does(string state, string expected)
    {
        GestureHints.For(state, "move", snapEnabled: false).ShouldContain(expected);
    }

    [Fact]
    public void A_drag_names_the_live_tool()
    {
        GestureHints.For("drag-manipulate", "move", false).ShouldStartWith("move");
        GestureHints.For("drag-manipulate", "rotate", false).ShouldStartWith("rotate");
        GestureHints.For("drag-manipulate", "resize", false).ShouldStartWith("resize");
    }

    [Fact]
    public void Alt_is_described_by_what_it_would_do_rather_than_by_its_name()
    {
        // Alt INVERTS the snap for a gesture, so what it does depends on the
        // state snapping is in. A hint that always said "Alt snaps" would be
        // wrong half the time.
        GestureHints.For("drag-manipulate", "move", snapEnabled: true).ShouldContain("Alt drags freely");
        GestureHints.For("drag-manipulate", "move", snapEnabled: false).ShouldContain("Alt snaps");
    }

    [Fact]
    public void Play_mode_says_nothing_because_its_own_chip_does()
    {
        GestureHints.For("suspended", "move", false).ShouldBeEmpty();
    }

    [Fact]
    public void An_unknown_state_reads_as_idle_rather_than_as_nothing()
    {
        // A blank status bar looks broken, and a state this table has not heard
        // of is a state where the general advice still applies.
        string idle = GestureHints.For("idle", "move", false);

        GestureHints.For("something-new", "move", false).ShouldBe(idle);
        GestureHints.For(null, "move", false).ShouldBe(idle);
        idle.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_shell_follows_the_snapshot_and_raises_only_on_a_change()
    {
        var model = new ShellModel();
        var raised = new List<string>();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellModel.GestureHint)) raised.Add(model.GestureHint);
        };

        model.ApplySnapshot(new FrameSnapshot { InteractionStateName = "look" });
        model.GestureHint.ShouldContain("look");

        // A steady state republishing at 30Hz must not raise per publish.
        int after = raised.Count;
        model.ApplySnapshot(new FrameSnapshot { InteractionStateName = "look" });
        raised.Count.ShouldBe(after);

        model.ApplySnapshot(new FrameSnapshot { InteractionStateName = "pan" });
        raised.Count.ShouldBeGreaterThan(after);
    }
}
