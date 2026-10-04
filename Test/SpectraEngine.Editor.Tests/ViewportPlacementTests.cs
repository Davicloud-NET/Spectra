using SpectraEngine.Core.Graphics;
using SpectraEngine.Editor.Viewport;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

// A native child must never land in a dock tool: re-parenting a
// NativeControlHost destroys its HWND and the engine session with it.
public sealed class ViewportPlacementTests
{
    private static ViewportDecision Composited => new(
        UseComposition: true,
        ViewportChoiceReason.ExplicitComposition,
        ViewportModePolicy.Describe(ViewportChoiceReason.ExplicitComposition));

    private static ViewportDecision Native => new(
        UseComposition: false,
        ViewportChoiceReason.ExplicitNative,
        ViewportModePolicy.Describe(ViewportChoiceReason.ExplicitNative));

    [Fact]
    public void Every_placement_owes_rules_and_a_sentence()
    {
        foreach (ViewportPlacement placement in Enum.GetValues<ViewportPlacement>())
        {
            // Throws for an unknown placement.
            _ = ViewportLayout.RulesFor(placement);

            ViewportLayout.Describe(placement).ShouldNotBeNullOrWhiteSpace(
                $"{placement} has no sentence, so a session laid out that way would be laid out silently.");
        }
    }

    [Fact]
    public void A_placement_that_is_not_a_placement_is_refused_rather_than_defaulted()
    {
        const ViewportPlacement Invented = (ViewportPlacement)99;

        Should.Throw<ArgumentOutOfRangeException>(() => ViewportLayout.RulesFor(Invented));
        Should.Throw<ArgumentOutOfRangeException>(() => ViewportLayout.Describe(Invented));
    }

    [Fact]
    public void Every_placement_is_reached_by_some_decision()
    {
        ViewportDecision[] decisions = [Native, Composited];

        HashSet<ViewportPlacement> reached = [.. decisions.Select(d => ViewportLayout.For(d))];

        reached.ShouldBe(Enum.GetValues<ViewportPlacement>().ToHashSet(), ignoreOrder: true);
    }

    [Fact]
    public void A_native_session_pins_the_viewport()
    {
        ViewportLayout.For(Native).ShouldBe(ViewportPlacement.PinnedCell);
    }

    [Fact]
    public void A_composited_session_docks_the_viewport()
    {
        ViewportLayout.For(Composited).ShouldBe(ViewportPlacement.DockedTool);
    }

    [Fact]
    public void The_reason_never_changes_the_placement_only_the_answer_does()
    {
        foreach (ViewportChoiceReason reason in Enum.GetValues<ViewportChoiceReason>())
        {
            var native = new ViewportDecision(false, reason, ViewportModePolicy.Describe(reason));
            var composited = new ViewportDecision(true, reason, ViewportModePolicy.Describe(reason));

            ViewportLayout.For(native).ShouldBe(ViewportPlacement.PinnedCell);
            ViewportLayout.For(composited).ShouldBe(ViewportPlacement.DockedTool);
        }
    }

    [Fact]
    public void A_pinned_viewport_may_not_float_or_pin_and_owns_its_airspace()
    {
        ViewportPlacementRules rules = ViewportLayout.RulesFor(ViewportPlacement.PinnedCell);

        rules.Docked.ShouldBeFalse();
        rules.CanFloat.ShouldBeFalse();

        // A pinned flyout draws in the window's Avalonia layer, under the
        // native child.
        rules.CanPin.ShouldBeFalse();

        rules.ToolsMayShareTheWindow.ShouldBeFalse();
    }

    [Fact]
    public void A_docked_viewport_floats_pins_and_gives_the_window_back()
    {
        ViewportPlacementRules rules = ViewportLayout.RulesFor(ViewportPlacement.DockedTool);

        rules.Docked.ShouldBeTrue();
        rules.CanFloat.ShouldBeTrue();
        rules.CanPin.ShouldBeTrue();

        rules.ToolsMayShareTheWindow.ShouldBeTrue();
    }

    [Fact]
    public void The_viewport_never_closes_in_either_placement()
    {
        // Nothing in the shell could reopen it.
        ViewportLayout.ViewportCanClose.ShouldBeFalse();
    }

    [Fact]
    public void Pinning_is_permitted_exactly_where_the_managed_layer_is_usable()
    {
        foreach (ViewportPlacement placement in Enum.GetValues<ViewportPlacement>())
        {
            ViewportPlacementRules rules = ViewportLayout.RulesFor(placement);
            rules.CanPin.ShouldBe(rules.ToolsMayShareTheWindow);
        }
    }
}
