using System.Numerics;
using SpectraEngine.Core.Scene;
using static SpectraEngine.Bsp.Tests.SpatialTestHelpers;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The scene's tree of boxes stays about as tall as the logarithm of its
/// leaves, whatever order nodes come and go in. A level that spawns parts in
/// a row then pays the same for the thousandth part as for the first.
/// </summary>
public sealed class SceneBvhBalanceTests
{
    private const int Parts = 4096;

    // A balanced tree of 4096 leaves is 12 tall. A list would be 4095.
    private const int Tall = 24;

    [Theory]
    [InlineData(3f)]
    [InlineData(0f)]
    public void A_row_of_parts_added_one_after_another_makes_no_list(float spacing)
    {
        var scene = new Scene("Row");
        for (int i = 0; i < Parts; i++)
            CreateBrushNode(scene.Root, $"p{i}", new Vector3(i * spacing, 0f, 0f));

        scene.Bvh.LeafCount.ShouldBe(Parts);
        scene.Bvh.Height.ShouldBeLessThanOrEqualTo(Tall);
        scene.Bvh.Validate();
    }

    [Fact]
    public void A_tree_that_parts_leave_and_join_from_one_end_stays_in_balance_and_answers_as_before()
    {
        var scene = new Scene("Churn");
        var live = new Queue<SceneNode>();
        for (int i = 0; i < Parts; i++)
            live.Enqueue(CreateBrushNode(scene.Root, $"p{i}", new Vector3(i * 3f, 0f, 0f)));

        // The oldest goes and a new one comes at the far end, as bullets do.
        for (int i = Parts; i < 3 * Parts; i++)
        {
            scene.Root.RemoveChild(live.Dequeue());
            live.Enqueue(CreateBrushNode(scene.Root, $"p{i}", new Vector3(i * 3f, 0f, 0f)));
        }

        scene.Bvh.LeafCount.ShouldBe(Parts);
        scene.Bvh.Height.ShouldBeLessThanOrEqualTo(Tall);
        scene.Bvh.Validate();

        SceneNode first = live.Peek();
        var ray = new Ray3(first.WorldPosition + new Vector3(0f, 10f, 0f), -Vector3.UnitY);
        scene.Raycast(ray, out SceneRaycastHit hit).ShouldBeTrue();
        hit.Node.ShouldBeSameAs(first);
    }

    [Fact]
    public void A_tree_that_moving_parts_are_put_into_again_stays_in_balance()
    {
        var scene = new Scene("Movers");
        var parts = new SceneNode[512];
        for (int i = 0; i < parts.Length; i++)
            parts[i] = CreateBrushNode(scene.Root, $"p{i}", new Vector3(i * 3f, 0f, 0f));

        for (int step = 1; step <= 20; step++)
        {
            for (int i = 0; i < parts.Length; i++)
                parts[i].LocalPosition = new Vector3(i * 3f, step * 5f, (i % 7) * step);

            scene.Bvh.Validate();
        }

        scene.Bvh.Height.ShouldBeLessThanOrEqualTo(Tall);
    }
}
