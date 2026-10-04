using System;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A doorway cut flush to the base of its wall, with a floor reaching the same
/// plane, must compile open.
/// </summary>
// Needs both conditions: the cut flush through the wall's bottom plane and
// another additive brush on that plane. The failure is a missing threshold
// face under the doorway, which lets the solid leak in from the floor.
public sealed class CoplanarCutSealingTests
{
    [Fact]
    public void A_doorway_cut_to_the_base_of_its_wall_is_open()
    {
        Assert.False(Build(floorTouching: true).ContainsPoint(new Vector3(0f, 1.2f, -4.25f)));
    }

    [Fact]
    public void The_same_doorway_with_the_floor_a_hair_lower_is_open()
    {
        // Control: same cut and wall, floor not on the wall's base plane.
        Assert.False(Build(floorTouching: false).ContainsPoint(new Vector3(0f, 1.2f, -4.25f)));
    }

    [Fact]
    public void Either_way_the_carve_emits_both_jambs()
    {
        Assert.Equal(2, Jambs(Build(floorTouching: true)));
        Assert.Equal(2, Jambs(Build(floorTouching: false)));
    }

    [Fact]
    public void The_threshold_under_the_doorway_exists()
    {
        // An upward face at the wall's base, spanning the opening.
        CsgWorld world = Build(floorTouching: true);

        int thresholds = 0;
        foreach (Polygon polygon in world.Surfaces)
        {
            if (polygon.Surface.Normal.Y < 0.99f)
                continue;
            if (MathF.Abs(polygon.Bounds.Min.Y) > 1e-3f)
                continue;

            // Inside the doorway's footprint.
            if (polygon.Bounds.Min.X >= -1.001f && polygon.Bounds.Max.X <= 1.001f &&
                polygon.Bounds.Min.Z >= -4.501f && polygon.Bounds.Max.Z <= -3.999f)
            {
                thresholds++;
            }
        }

        Assert.True(thresholds >= 1,
            "the doorway has no floor at its base, so the compiled skin is open there");
    }

    public static CsgWorld Build(bool floorTouching)
    {
        var scene = new Scene("CoplanarCut");

        void Box(string name, Vector3 center, Vector3 half, bool cut = false)
        {
            SceneNode node = scene.Root.CreateChild(name);
            node.LocalPosition = center;
            Brush brush = Brush.CreateBox(-half, half);
            node.Brush = cut ? brush.WithOperation(BrushOperation.Subtractive) : brush;
        }

        // Wall y in [0, 3]. The cut reaches y = 0 and is flush through the
        // wall in z. Floor top is at 0 or a hundredth below.
        Box("wall", new Vector3(0f, 1.5f, -4.25f), new Vector3(6f, 1.5f, 0.25f));
        Box("door", new Vector3(0f, 1.2f, -4.25f), new Vector3(1f, 1.2f, 0.25f), cut: true);
        Box("floor", new Vector3(0f, floorTouching ? -0.5f : -0.51f, 0f), new Vector3(6f, 0.5f, 6f));

        scene.RebuildStaticWorld(new FakeRenderer());
        return scene.StaticWorld!;
    }

    private static int Jambs(CsgWorld world)
    {
        int count = 0;
        foreach (Polygon polygon in world.Surfaces)
        {
            if (MathF.Abs(MathF.Abs(polygon.Surface.Normal.X) - 1f) > 1e-3f) continue;
            if (MathF.Abs(MathF.Abs(polygon.Surface.D) - 1f) > 1e-3f) continue;
            count++;
        }
        return count;
    }
}
