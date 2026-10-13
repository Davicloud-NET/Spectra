using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A part brush that leaves before its mesh was ever built is let go of at
/// once. A host that draws nothing never pumps the cache, and a level that
/// spawns and destroys parts there must not keep every brush it had.
/// </summary>
public sealed class PartBrushMeshUnbuiltTests
{
    [Fact]
    public void A_brush_that_was_never_built_is_gone_with_its_last_node_and_waits_for_no_pump()
    {
        var cache = new PartBrushMeshCache();
        var node = new SceneNode("Part");
        Brush brush = Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));

        cache.SetReference(node, brush);
        cache.PendingCount.ShouldBe(1);
        cache.SetReference(node, null);

        cache.PendingCount.ShouldBe(0);
        cache.TryGet(brush, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_brush_that_was_built_keeps_its_mesh_until_the_next_pump_as_before()
    {
        var cache = new PartBrushMeshCache();
        var renderer = new FakeRenderer();
        var node = new SceneNode("Part");
        Brush brush = Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));
        cache.SetReference(node, brush);
        cache.Pump(renderer, static _ => null);
        cache.Count.ShouldBe(1);

        // Out and in again within one frame builds nothing anew.
        cache.SetReference(node, null);
        cache.SetReference(node, brush);
        cache.Pump(renderer, static _ => null);
        cache.Count.ShouldBe(1);
        renderer.CreatedMeshes.Count.ShouldBe(renderer.LiveMeshes.Count);

        cache.SetReference(node, null);
        cache.PendingCount.ShouldBe(1);
        cache.Pump(renderer, static _ => null);

        cache.Count.ShouldBe(0);
        renderer.LiveMeshes.ShouldBeEmpty();
    }

    [Fact]
    public void A_scene_no_query_is_made_of_keeps_no_part_that_moved_and_left()
    {
        var scene = new Scene("Headless");
        var gone = new List<WeakReference<SceneNode>>();
        for (int i = 0; i < 2000; i++)
            gone.Add(MovedAndLeft(scene, i));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // The lists of what has yet to be refit hold a few that left last.
        gone.Count(node => node.TryGetTarget(out _)).ShouldBeLessThan(200);
    }

    // In a method of its own, so no local keeps the node alive in the test.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference<SceneNode> MovedAndLeft(Scene scene, int at)
    {
        SceneNode part = EntityMotion.BrushNode(scene.Root, "Part", BrushKind.Part, new Vector3(at, 0f, 0f));
        part.LocalPosition = new Vector3(at, 5f, 0f);
        scene.Root.RemoveChild(part);
        return new WeakReference<SceneNode>(part);
    }
}
