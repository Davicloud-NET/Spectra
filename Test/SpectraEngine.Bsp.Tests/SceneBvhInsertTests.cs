using System.Numerics;
using SpectraEngine.Core.Scene;
using static SpectraEngine.Bsp.Tests.SpatialTestHelpers;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A leaf that gets a parent of its own over a tall side of the scene's tree
/// of boxes is turned into that side at once, and not by a later insert.
/// </summary>
public sealed class SceneBvhInsertTests
{
    [Fact]
    public void A_part_far_from_a_lopsided_tree_is_turned_into_it_at_once()
    {
        // Two near each other and a third further off: one side of the top
        // is a pair and the other a leaf.
        var scene = new Scene("Far");
        CreateBrushNode(scene.Root, "a", new Vector3(0f, 0f, 0f));
        CreateBrushNode(scene.Root, "b", new Vector3(2f, 0f, 0f));
        CreateBrushNode(scene.Root, "c", new Vector3(20f, 0f, 0f));
        scene.Bvh.Height.ShouldBe(2);

        // So far off that it gets a parent of its own over all three.
        CreateBrushNode(scene.Root, "d", new Vector3(2000f, 0f, 0f));

        scene.Bvh.LeafCount.ShouldBe(4);
        scene.Bvh.Height.ShouldBe(2, "four leaves fit in two levels");
        scene.Bvh.Validate();
    }
}
