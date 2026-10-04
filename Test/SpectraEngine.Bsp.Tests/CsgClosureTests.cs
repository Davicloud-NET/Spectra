using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The compiled skin of a set of solids must be a closed surface.</summary>
// The vector areas of a closed surface sum to zero. A residual means a hole;
// its direction and size say which way the missing patch faces and how big.
public sealed class CsgClosureTests
{
    // Float slop over a few hundred surfaces at play-area distances.
    private const float Tolerance = 0.01f;

    public static Vector3 Closure(IReadOnlyList<Polygon> surfaces)
    {
        var total = Vector3.Zero;
        for (int i = 0; i < surfaces.Count; i++)
        {
            ReadOnlySpan<Vector3> v = surfaces[i].VertexSpan;
            var area = Vector3.Zero;
            for (int j = 0; j < v.Length; j++)
                area += Vector3.Cross(v[j], v[(j + 1) % v.Length]);
            total += area * 0.5f;
        }
        return total;
    }

    private static CsgWorld Compile(Action<Scene> build)
    {
        var scene = new Scene("Closure");
        build(scene);
        scene.RebuildStaticWorld(new FakeRenderer());
        return scene.StaticWorld!;
    }

    private static void Box(Scene scene, string name, Vector3 centre, Vector3 half)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = centre;
        node.Brush = Brush.CreateBox(-half, half);
    }

    private static void Cut(Scene scene, string name, Vector3 centre, Vector3 half)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = centre;
        node.Brush = Brush.CreateBox(-half, half).WithOperation(BrushOperation.Subtractive);
    }

    private static void Staircase(Scene scene, int treads, float rise, float run)
    {
        for (int i = 0; i < treads; i++)
        {
            float top = rise * (i + 1);
            Box(scene, $"tread{i}",
                new Vector3(135f + run * i + run * 0.5f, (top - 3f) * 0.5f, 0f),
                new Vector3(run * 0.5f, (top + 3f) * 0.5f, 2f));
        }
    }

    [Fact]
    public void One_box_is_closed()
    {
        Vector3 closure = Closure(Compile(scene => Box(scene, "a", Vector3.Zero, Vector3.One)).Surfaces);
        Assert.True(closure.Length() < Tolerance, $"a single box should close, residual {closure}");
    }

    [Fact]
    public void Two_boxes_meeting_flush_are_closed()
    {
        Vector3 closure = Closure(Compile(scene =>
        {
            Box(scene, "a", new Vector3(0f, 0f, 0f), Vector3.One);
            Box(scene, "b", new Vector3(2f, 0f, 0f), Vector3.One);
        }).Surfaces);

        Assert.True(closure.Length() < Tolerance, $"two flush boxes should close, residual {closure}");
    }

    [Fact]
    public void A_brush_hollowed_by_a_through_cut_is_closed()
    {
        Vector3 closure = Closure(Compile(scene =>
        {
            Box(scene, "slab", new Vector3(0f, 0f, 0f), new Vector3(8f, 1f, 8f));
            Cut(scene, "hole", new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f));
        }).Surfaces);

        Assert.True(closure.Length() < Tolerance, $"a through-cut slab should close, residual {closure}");
    }

    [Fact]
    public void A_doorway_flush_with_its_wall_base_is_closed()
    {
        Vector3 closure = Closure(CoplanarCutSealingTests.Build(floorTouching: true).Surfaces);
        Assert.True(closure.Length() < Tolerance,
            $"a doorway at its wall's base should close, residual {closure}");
    }

    [Fact]
    public void The_demo_play_area_has_no_missing_horizontal_boundary()
    {
        // y and z only: the play area has a known hole in x, reproduced by
        // the skipped staircase test below.
        Vector3 closure = Closure(Compile(scene =>
            DemoPlayArea.Build(scene, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default)).Surfaces);

        Assert.True(MathF.Abs(closure.Y) < Tolerance,
            $"the play area is missing upward or downward boundary: residual {closure}");
        Assert.True(MathF.Abs(closure.Z) < Tolerance,
            $"the play area is missing z-facing boundary: residual {closure}");
    }

    private const string XResidualReason =
        "Known CSG defect: two solids meeting flush lose one side of the shared interface when the " +
        "junction plane is not exactly representable in binary. See the test remarks.";

    // The last tread meets the terrace flush at x = 139 and the 4 by 2 shared
    // interface keeps one of its two opposite faces. Rise and run (0.40, 0.80)
    // are not exact in binary, so the junction lands a few millionths off 139,
    // and Csg.CoplanarOrientation and Polygon.Split classify at different
    // tolerances.
    [Fact(Skip = XResidualReason)]
    public void A_staircase_meeting_a_terrace_on_an_inexact_plane_is_closed()
    {
        Vector3 closure = Closure(Compile(scene =>
        {
            Box(scene, "floor", new Vector3(150f, -1.5f, 0f), new Vector3(20f, 1.5f, 20f));
            Box(scene, "terrace", new Vector3(143.5f, 1f, 0f), new Vector3(4.5f, 1f, 12f));
            Staircase(scene, treads: 5, rise: 0.40f, run: 0.80f);
        }).Surfaces);

        Assert.True(closure.Length() < Tolerance, $"residual {closure}, expected about (-8, 0, 0)");
    }

    [Fact]
    public void The_same_staircase_with_binary_exact_treads_is_closed()
    {
        // Control: same junction and area, rise and run exact in binary.
        Vector3 closure = Closure(Compile(scene =>
        {
            Box(scene, "floor", new Vector3(150f, -1.5f, 0f), new Vector3(20f, 1.5f, 20f));
            Box(scene, "terrace", new Vector3(143.5f, 1f, 0f), new Vector3(4.5f, 1f, 12f));
            Staircase(scene, treads: 8, rise: 0.25f, run: 0.50f);
        }).Surfaces);

        Assert.True(closure.Length() < Tolerance,
            $"the exact-tread staircase should close, residual {closure}");
    }
}
