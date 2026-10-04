using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// Revealing a node picked in the viewport: which parents get expanded.
/// Also covers the tree's parent map.
/// </summary>
public sealed class SceneTreeRevealTests
{
    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid Leaf = Guid.NewGuid();
    private static readonly Guid Sibling = Guid.NewGuid();

    private static SceneTreeModel NewTree() => new(new EngineHost(NullLogger.Instance), NullLogger.Instance);

    private static SceneChange Added(Guid id, Guid parent, string name, int index) =>
        new(SceneChangeKind.Added, id, parent, name, index, SceneNodeKind.Empty);

    private static SceneTreeModel Nested()
    {
        SceneTreeModel tree = NewTree();
        tree.ApplyChanges(new FrameSnapshot
        {
            FrameNumber = 1,
            Changes = new[]
            {
                Added(Root, Guid.Empty, "Root", -1),
                Added(Branch, Root, "Branch", 0),
                Added(Leaf, Branch, "Leaf", 0),
                Added(Sibling, Root, "Sibling", 1),
            },
        });

        // Top-level rows open by default; these tests need a closed start.
        tree.ToggleExpanded(tree.Roots[0]);
        return tree;
    }

    private static SceneTreeNode Find(SceneTreeModel tree, string name) =>
        Walk(tree.Roots).First(n => n.Name == name);

    private static IEnumerable<SceneTreeNode> Walk(IEnumerable<SceneTreeNode> nodes)
    {
        foreach (SceneTreeNode node in nodes)
        {
            yield return node;
            foreach (SceneTreeNode child in Walk(node.Children))
                yield return child;
        }
    }

    [Fact]
    public void Revealing_a_node_expands_every_parent_above_it()
    {
        SceneTreeModel tree = Nested();

        tree.TryReveal(Leaf, out SceneTreeNode node).ShouldBeTrue();

        node.Name.ShouldBe("Leaf");
        Find(tree, "Root").IsExpanded.ShouldBeTrue();
        Find(tree, "Branch").IsExpanded.ShouldBeTrue();
    }

    [Fact]
    public void Revealing_a_node_does_not_expand_the_node_itself()
    {
        SceneTreeModel tree = Nested();

        tree.TryReveal(Branch, out _).ShouldBeTrue();

        Find(tree, "Branch").IsExpanded.ShouldBeFalse();
        Find(tree, "Root").IsExpanded.ShouldBeTrue();
    }

    [Fact]
    public void Revealing_never_collapses_anything()
    {
        SceneTreeModel tree = Nested();
        Find(tree, "Sibling").IsExpanded = true;

        tree.TryReveal(Leaf, out _).ShouldBeTrue();

        Find(tree, "Sibling").IsExpanded.ShouldBeTrue();
    }

    [Fact]
    public void A_root_node_reveals_with_nothing_to_expand()
    {
        SceneTreeModel tree = Nested();

        tree.TryReveal(Root, out SceneTreeNode node).ShouldBeTrue();

        node.Name.ShouldBe("Root");
        node.IsExpanded.ShouldBeFalse();
    }

    [Fact]
    public void An_unknown_id_reveals_nothing_rather_than_throwing()
    {
        // Normal lag: the selection can name a node whose Added change has not drained yet.
        SceneTreeModel tree = Nested();

        tree.TryReveal(Guid.NewGuid(), out _).ShouldBeFalse();
        Find(tree, "Root").IsExpanded.ShouldBeFalse();
    }

    [Fact]
    public void A_reparented_node_reveals_through_its_new_chain()
    {
        SceneTreeModel tree = Nested();

        tree.ApplyChanges(new FrameSnapshot
        {
            FrameNumber = 2,
            Changes = new[]
            {
                new SceneChange(SceneChangeKind.Reparented, Leaf, Sibling, "Leaf", 0, SceneNodeKind.Empty),
            },
        });

        tree.TryReveal(Leaf, out _).ShouldBeTrue();

        Find(tree, "Sibling").IsExpanded.ShouldBeTrue();
        Find(tree, "Branch").IsExpanded.ShouldBeFalse("the old parent is no longer on the chain");
    }

    [Fact]
    public void A_removed_subtree_leaves_no_parentage_behind()
    {
        SceneTreeModel tree = Nested();

        tree.ApplyChanges(new FrameSnapshot
        {
            FrameNumber = 2,
            Changes = new[]
            {
                new SceneChange(SceneChangeKind.Removed, Branch, Guid.Empty, "Branch", -1, SceneNodeKind.Empty),
            },
        });

        tree.TryReveal(Leaf, out _).ShouldBeFalse();
        tree.TryReveal(Branch, out _).ShouldBeFalse();
        tree.Count.ShouldBe(2);
    }

    [Fact]
    public void A_node_moved_to_the_top_level_has_no_chain_to_expand()
    {
        SceneTreeModel tree = Nested();

        tree.ApplyChanges(new FrameSnapshot
        {
            FrameNumber = 2,
            Changes = new[]
            {
                new SceneChange(SceneChangeKind.Reparented, Leaf, Guid.Empty, "Leaf", 1, SceneNodeKind.Empty),
            },
        });

        tree.TryReveal(Leaf, out SceneTreeNode node).ShouldBeTrue();
        tree.Roots.ShouldContain(node);
        Find(tree, "Branch").IsExpanded.ShouldBeFalse();
    }
}
