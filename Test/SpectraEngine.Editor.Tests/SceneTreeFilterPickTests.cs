using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editor.Shell;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Which filter match the filter box reveals and selects: the first in tree
/// order, then the next and the one before.
/// </summary>
// The panel's half, the scroll and the selection request, is in the render suite.
public sealed class SceneTreeFilterPickTests
{
    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid Hall = Guid.NewGuid();
    private static readonly Guid Lamp = Guid.NewGuid();
    private static readonly Guid HumDeep = Guid.NewGuid();
    private static readonly Guid Yard = Guid.NewGuid();
    private static readonly Guid HumLate = Guid.NewGuid();
    private static readonly Guid HumEarly = Guid.NewGuid();

    private static SceneChange Added(Guid id, Guid parent, string name, int index) =>
        new(SceneChangeKind.Added, id, parent, name, index, SceneNodeKind.Empty);

    private static FrameSnapshot Batch(long frame, params SceneChange[] changes) =>
        new() { FrameNumber = frame, Changes = changes };

    // Root
    //   Hall
    //     Lamp
    //       HumDeep
    //   Yard
    //   HumEarly
    //   HumLate
    //
    // Only Root is open. HumEarly is added last and lands above HumLate, so
    // the order the nodes arrived in is not the order they read in.
    private static SceneTreeModel Level()
    {
        var tree = new SceneTreeModel(new EngineHost(NullLogger.Instance), NullLogger.Instance);
        tree.ApplyChanges(Batch(1,
            Added(Root, Guid.Empty, "Root", -1),
            Added(HumLate, Root, "HumLate", 0),
            Added(Yard, Root, "Yard", 0),
            Added(Hall, Root, "Hall", 0),
            Added(Lamp, Hall, "Lamp", 0),
            Added(HumDeep, Lamp, "HumDeep", 0),
            Added(HumEarly, Root, "HumEarly", 2)));
        return tree;
    }

    private static SceneTreeNode Node(SceneTreeModel tree, Guid id)
    {
        tree.TryGetNode(id, out SceneTreeNode node).ShouldBeTrue();
        return node;
    }

    private static string[] Rows(SceneTreeModel tree) => [.. tree.Rows.Select(r => r.Name)];

    private static string? Name(SceneTreeNode? node) => node?.Name;

    [Fact]
    public void The_first_match_is_the_one_highest_up_the_tree()
    {
        SceneTreeModel tree = Level();
        tree.ApplyFilter("hum");

        // Three levels down, but its row comes before the two under Root.
        Name(tree.RevealFirstMatch()).ShouldBe("HumDeep");
    }

    [Fact]
    public void Among_siblings_the_first_match_follows_sibling_order_not_arrival()
    {
        SceneTreeModel tree = Level();
        tree.ApplyFilter("hum");

        Name(tree.PickMatch(0)).ShouldBe("HumDeep");
        Name(tree.PickMatch(1)).ShouldBe("HumEarly");
        Name(tree.PickMatch(1)).ShouldBe("HumLate");
    }

    [Fact]
    public void A_match_under_a_collapsed_parent_gets_its_parents_opened()
    {
        SceneTreeModel tree = Level();
        Rows(tree).ShouldBe(["Root", "Hall", "Yard", "HumEarly", "HumLate"]);
        tree.ApplyFilter("hum");

        SceneTreeNode first = tree.RevealFirstMatch().ShouldNotBeNull();

        tree.Rows.ShouldContain(first);
        Rows(tree).ShouldBe(["Root", "Hall", "Lamp", "HumDeep", "Yard", "HumEarly", "HumLate"]);
        first.IsExpanded.ShouldBeFalse("only the parents open");
    }

    [Fact]
    public void Filtering_alone_opens_nothing()
    {
        SceneTreeModel tree = Level();

        tree.ApplyFilter("hum");

        Node(tree, Hall).IsExpanded.ShouldBeFalse();
        Rows(tree).ShouldBe(["Root", "Hall", "Yard", "HumEarly", "HumLate"]);
    }

    [Fact]
    public void Enter_picks_the_first_match_and_gives_it_a_row()
    {
        SceneTreeModel tree = Level();
        tree.ApplyFilter("hum");

        SceneTreeNode picked = tree.PickMatch(0).ShouldNotBeNull();

        picked.Id.ShouldBe(HumDeep);
        tree.IsRowVisible(picked).ShouldBeTrue("the list can only select a row it has");
    }

    [Fact]
    public void Enter_again_stays_on_the_same_match()
    {
        SceneTreeModel tree = Level();
        tree.ApplyFilter("hum");
        tree.PickMatch(0);
        tree.PickMatch(1);

        Name(tree.PickMatch(0)).ShouldBe("HumEarly");
    }

    [Fact]
    public void Down_from_new_filter_text_starts_at_the_first_match()
    {
        // Nothing marks the first match until it is selected, so Down must
        // not skip it.
        SceneTreeModel tree = Level();
        tree.ApplyFilter("hum");

        Name(tree.PickMatch(1)).ShouldBe("HumDeep");
    }

    [Fact]
    public void Up_from_new_filter_text_starts_at_the_last_match()
    {
        SceneTreeModel tree = Level();
        tree.ApplyFilter("hum");

        Name(tree.PickMatch(-1)).ShouldBe("HumLate");
    }

    [Fact]
    public void Down_and_Up_wrap_at_the_ends()
    {
        SceneTreeModel tree = Level();
        tree.ApplyFilter("hum");
        tree.PickMatch(0);

        Name(tree.PickMatch(-1)).ShouldBe("HumLate");
        Name(tree.PickMatch(1)).ShouldBe("HumDeep");
    }

    [Fact]
    public void New_filter_text_starts_again_at_the_first_match()
    {
        SceneTreeModel tree = Level();
        tree.ApplyFilter("hum");
        tree.PickMatch(-1);

        tree.ApplyFilter("humL");
        tree.ApplyFilter("hum");

        Name(tree.PickMatch(0)).ShouldBe("HumDeep");
    }

    [Fact]
    public void A_scene_change_under_the_same_filter_keeps_the_pick()
    {
        // The tree runs the filter again after every batch of changes.
        SceneTreeModel tree = Level();
        tree.ApplyFilter("hum");
        tree.PickMatch(0);
        tree.PickMatch(1);

        tree.ApplyChanges(Batch(2, Added(Guid.NewGuid(), Yard, "HumNew", 0)));

        Name(tree.PickMatch(1)).ShouldBe("HumLate");
    }

    [Fact]
    public void A_pick_that_left_the_scene_falls_back_to_the_first_match()
    {
        SceneTreeModel tree = Level();
        tree.ApplyFilter("hum");
        Name(tree.PickMatch(-1)).ShouldBe("HumLate");

        tree.ApplyChanges(Batch(2,
            new SceneChange(SceneChangeKind.Removed, HumLate, Guid.Empty, "HumLate", -1, SceneNodeKind.Empty)));

        Name(tree.PickMatch(1)).ShouldBe("HumDeep");
    }

    [Fact]
    public void No_match_picks_nothing_and_opens_nothing()
    {
        SceneTreeModel tree = Level();
        tree.ApplyFilter("nothing is called this");

        tree.RevealFirstMatch().ShouldBeNull();
        tree.PickMatch(0).ShouldBeNull();
        tree.PickMatch(1).ShouldBeNull();
        tree.PickMatch(-1).ShouldBeNull();

        Rows(tree).ShouldBe(["Root", "Hall", "Yard", "HumEarly", "HumLate"]);
    }

    [Fact]
    public void An_unknown_kind_picks_nothing()
    {
        SceneTreeModel tree = Level();
        tree.ApplyFilter("t:enttiy");

        tree.PickMatch(0).ShouldBeNull();
    }

    [Fact]
    public void With_no_filter_there_is_nothing_to_pick()
    {
        // Every node counts as a match then, and Enter must not select the root.
        SceneTreeModel tree = Level();

        tree.RevealFirstMatch().ShouldBeNull();
        tree.PickMatch(0).ShouldBeNull();

        tree.ApplyFilter("hum");
        tree.ApplyFilter("");

        tree.PickMatch(0).ShouldBeNull();
    }

    [Fact]
    public void The_shell_tells_the_panel_which_row_a_key_picked()
    {
        SceneTreeModel tree = Level();
        var shell = new ShellModel { Tree = tree };
        var revealed = new List<SceneTreeNode>();
        shell.FilterMatchRevealed += revealed.Add;
        tree.ApplyFilter("hum");

        SceneTreeNode picked = shell.PickFilterMatch(0).ShouldNotBeNull();

        picked.Id.ShouldBe(HumDeep);
        revealed.ShouldBe([picked]);
    }

    [Fact]
    public void The_shell_reveals_nothing_when_nothing_matches()
    {
        SceneTreeModel tree = Level();
        var shell = new ShellModel { Tree = tree };
        var revealed = new List<SceneTreeNode>();
        shell.FilterMatchRevealed += revealed.Add;
        tree.ApplyFilter("nothing is called this");

        shell.PickFilterMatch(0).ShouldBeNull();
        shell.PickFilterMatch(1).ShouldBeNull();

        revealed.ShouldBeEmpty();
    }
}
