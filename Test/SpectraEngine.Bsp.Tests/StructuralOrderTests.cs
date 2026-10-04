using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Sibling index decides traversal order, which decides placement order, which
/// decides the compiled geometry.
/// </summary>
// The other determinism tests build BrushPlacement[] by hand and never go
// through the scene graph, so they can't see a node returning at the wrong index.
public sealed class StructuralOrderTests
{
    [Fact]
    public void Restoring_a_middle_sibling_at_its_index_rebuilds_the_world_bit_for_bit()
    {
        var scene = new Scene("Structural");
        SceneNode[] nodes = BuildOverlappingRow(scene, count: 5);

        (float[] Vertices, uint[] Indices) before = Compile(scene);
        Guid[] orderBefore = PlacementOrder(scene);

        // What RemoveNodesCommand and its undo do to the graph.
        SceneNode removed = nodes[2];
        int index = removed.IndexInParent;
        index.ShouldBe(2);

        scene.Root.RemoveChild(removed);
        scene.Root.InsertChild(index, removed);

        PlacementOrder(scene).ShouldBe(orderBefore);

        (float[] Vertices, uint[] Indices) after = Compile(scene);
        after.Vertices.ShouldBe(before.Vertices);
        after.Indices.ShouldBe(before.Indices);
    }

    [Fact]
    public void Appending_the_same_sibling_back_puts_it_in_a_different_placement_slot()
    {
        // Asserts on placement order, not floats: whether a reorder changes the
        // geometry depends on which brushes overlap.
        var scene = new Scene("Structural");
        SceneNode[] nodes = BuildOverlappingRow(scene, count: 5);
        Guid[] orderBefore = PlacementOrder(scene);

        SceneNode removed = nodes[2];
        scene.Root.RemoveChild(removed);
        scene.Root.AddChild(removed);

        Guid[] orderAfter = PlacementOrder(scene);
        orderAfter.ShouldNotBe(orderBefore);
        orderAfter[^1].ShouldBe(removed.Id);
    }

    [Fact]
    public void A_clone_inserted_at_an_index_takes_that_placement_slot()
    {
        var scene = new Scene("Structural");
        SceneNode[] nodes = BuildOverlappingRow(scene, count: 4);

        SceneNode clone = nodes[1].Clone();
        scene.Root.InsertChild(1, clone);

        Guid[] order = PlacementOrder(scene);
        order.Length.ShouldBe(5);
        order[1].ShouldBe(clone.Id);
        order[2].ShouldBe(nodes[1].Id);

        // Own brush instance: the carve cache keys on reference identity.
        clone.Brush.ShouldNotBeSameAs(nodes[1].Brush);
        clone.Brush!.LocalBounds.Min.ShouldBe(nodes[1].Brush!.LocalBounds.Min);
        clone.Brush.LocalBounds.Max.ShouldBe(nodes[1].Brush.LocalBounds.Max);
    }

    [Fact]
    public void Insert_maintains_the_subtree_counters_exactly_as_append_does()
    {
        var scene = new Scene("Structural");
        SceneNode group = scene.Root.CreateChild("Group");

        SceneNode brush = new SceneNode("Brush") { Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f)) };
        SceneNode part = new SceneNode("Part")
        {
            Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f)),
            BrushKind = BrushKind.Part,
        };

        group.InsertChild(0, part);
        group.InsertChild(0, brush);

        group.Children[0].ShouldBeSameAs(brush);
        group.SubtreeBrushCount.ShouldBe(2);
        group.SubtreeStaticWorldBrushCount.ShouldBe(1);
        scene.Root.SubtreeBrushCount.ShouldBe(2);
        scene.Root.SubtreeStaticWorldBrushCount.ShouldBe(1);

        group.RemoveChild(brush);
        group.SubtreeBrushCount.ShouldBe(1);
        group.SubtreeStaticWorldBrushCount.ShouldBe(0);
        scene.Root.SubtreeBrushCount.ShouldBe(1);
    }

    [Fact]
    public void Attaching_a_node_under_itself_or_its_own_descendant_is_refused()
    {
        // A cycle would hang the next graph walk.
        var scene = new Scene("Structural");
        SceneNode parent = scene.Root.CreateChild("Parent");
        SceneNode child = parent.CreateChild("Child");

        Should.Throw<ArgumentException>(() => parent.AddChild(parent));
        Should.Throw<ArgumentException>(() => child.AddChild(parent));
        Should.Throw<ArgumentException>(() => child.InsertChild(0, parent));

        // Parent taking its own child again is a reorder, not a cycle.
        parent.InsertChild(0, child).ShouldBeSameAs(child);
    }

    // Each box overlaps its neighbour, so the carve has to order them.
    private static SceneNode[] BuildOverlappingRow(Scene scene, int count)
    {
        var nodes = new SceneNode[count];
        for (int i = 0; i < count; i++)
        {
            SceneNode node = scene.Root.CreateChild($"Brush{i}");
            node.LocalPosition = new Vector3(i * 1.5f, 0f, 0f);
            node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
            nodes[i] = node;
        }

        return nodes;
    }

    // Traversal order is the order placements are appended in.
    private static Guid[] PlacementOrder(Scene scene) =>
        [.. scene.Root.Traverse().Where(n => n.IsStaticWorldBrush).Select(n => n.Id)];

    private static (float[] Vertices, uint[] Indices) Compile(Scene scene)
    {
        scene.RebuildStaticWorld(new FakeRenderer());
        scene.StaticWorld.ShouldNotBeNull();
        return scene.StaticWorld!.BuildMesh();
    }
}
