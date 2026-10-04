using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Subtractive brushes: the compiled solid is <c>⋃{additive} \ ⋃{subtractive}</c>,
/// unordered.
/// </summary>
// Asserts through ContainsPoint, not surface counts: a surface set that is
// not closed can still have every polygon correctly oriented.
public sealed class NegativeBrushTests
{
    [Fact]
    public void A_brush_is_additive_unless_it_says_otherwise()
    {
        Box(-1f, 1f).Operation.ShouldBe(BrushOperation.Additive);
    }

    [Fact]
    public void An_equal_operation_write_returns_the_same_instance()
    {
        // The carve cache keys on reference identity.
        Brush brush = Box(-1f, 1f);

        brush.WithOperation(BrushOperation.Additive).ShouldBeSameAs(brush);
    }

    [Fact]
    public void A_changed_operation_returns_a_new_instance_with_identical_geometry()
    {
        Brush additive = Box(-1f, 1f);
        Brush subtractive = additive.WithOperation(BrushOperation.Subtractive);

        subtractive.ShouldNotBeSameAs(additive);
        subtractive.Operation.ShouldBe(BrushOperation.Subtractive);
        subtractive.LocalPlanes.Count.ShouldBe(additive.LocalPlanes.Count);
        subtractive.LocalFaces.Count.ShouldBe(additive.LocalFaces.Count);
        subtractive.LocalBounds.Min.ShouldBe(additive.LocalBounds.Min);
        subtractive.LocalBounds.Max.ShouldBe(additive.LocalBounds.Max);
        subtractive.Transform.ShouldBe(additive.Transform);
    }

    [Fact]
    public void The_operation_survives_a_resize()
    {
        // Otherwise resizing a hole turns it into a solid block.
        Brush hole = Box(-1f, 1f).WithOperation(BrushOperation.Subtractive);

        hole.WithScaledExtents(new Vector3(2f, 2f, 2f)).Operation
            .ShouldBe(BrushOperation.Subtractive);
    }

    [Fact]
    public void The_operation_survives_a_retexture()
    {
        Brush hole = Box(-1f, 1f).WithOperation(BrushOperation.Subtractive);

        hole.WithFaceMaterial(0, MaterialRegistry.Intern("Materials/negative_retexture.spectramat")).Operation
            .ShouldBe(BrushOperation.Subtractive);
    }

    [Fact]
    public void Flipping_a_polygon_reverses_the_winding_AND_negates_the_plane()
    {
        var verts = new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
            new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
        };
        var polygon = new Polygon(verts, new Plane(new Vector3(0f, 0f, 1f), 0f));

        Polygon flipped = polygon.Flipped();

        flipped.Surface.Normal.ShouldBe(new Vector3(0f, 0f, -1f));
        flipped.VertexCount.ShouldBe(4);
        for (int i = 0; i < 4; i++)
            flipped.Vertices[i].ShouldBe(verts[3 - i]);
    }

    [Fact]
    public void A_negative_brush_removes_solid()
    {
        // A 2-unit notch bitten out of the +x end of a 10-unit bar.
        CsgWorld world = Build(
            Box(new Vector3(0f, 0f, 0f), new Vector3(10f, 2f, 2f)),
            Negative(new Vector3(8f, 0f, 0f), new Vector3(10f, 2f, 2f)));

        world.ContainsPoint(new Vector3(5f, 1f, 1f)).ShouldBeTrue("the uncut part is still solid");
        world.ContainsPoint(new Vector3(9f, 1f, 1f)).ShouldBeFalse("the notch was removed");
    }

    [Fact]
    public void A_negative_brush_emits_no_skin_of_its_own()
    {
        // A negative floating in empty space.
        CsgWorld world = Build(Negative(new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f)));

        world.Surfaces.Count.ShouldBe(0);
    }

    [Fact]
    public void A_cavity_fully_inside_a_solid_is_hollow_and_still_closed()
    {
        // The negative touches none of the cut brush's planes, so the cavity
        // is bounded only by new geometry.
        CsgWorld world = Build(
            Box(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f)),
            Negative(new Vector3(4f, 4f, 4f), new Vector3(6f, 6f, 6f)));

        world.ContainsPoint(new Vector3(5f, 5f, 5f)).ShouldBeFalse("the cavity is hollow");
        world.ContainsPoint(new Vector3(2f, 5f, 5f)).ShouldBeTrue("the shell is still solid");
        world.ContainsPoint(new Vector3(5f, 2f, 5f)).ShouldBeTrue();
        world.ContainsPoint(new Vector3(5f, 5f, 8f)).ShouldBeTrue();
        world.ContainsPoint(new Vector3(15f, 5f, 5f)).ShouldBeFalse("outside is still outside");
    }

    [Fact]
    public void A_flush_through_cut_opens_a_doorway_through_both_faces()
    {
        // The negative's ±z planes coincide with the slab's: same-facing coplanar.
        CsgWorld world = Build(
            Box(new Vector3(0f, 0f, 0f), new Vector3(10f, 8f, 1f)),
            Negative(new Vector3(4f, 0f, 0f), new Vector3(6f, 5f, 1f)));

        world.ContainsPoint(new Vector3(5f, 2f, 0.5f)).ShouldBeFalse("the doorway is open");
        world.ContainsPoint(new Vector3(5f, 7f, 0.5f)).ShouldBeTrue("the lintel above it is solid");
        world.ContainsPoint(new Vector3(1f, 2f, 0.5f)).ShouldBeTrue("the jamb beside it is solid");
        world.ContainsPoint(new Vector3(9f, 2f, 0.5f)).ShouldBeTrue();
    }

    [Fact]
    public void A_negative_resting_on_a_face_removes_nothing()
    {
        // Coincident and opposite-facing. The additive interior-interface rule
        // would delete the slab's face under the negative and leave it open.
        CsgWorld world = Build(
            Box(new Vector3(0f, 0f, 0f), new Vector3(10f, 2f, 10f)),
            Negative(new Vector3(3f, 2f, 3f), new Vector3(7f, 5f, 7f)));

        world.ContainsPoint(new Vector3(5f, 1f, 5f)).ShouldBeTrue("the slab is untouched");
        world.ContainsPoint(new Vector3(5f, 0.1f, 5f)).ShouldBeTrue();
        world.ContainsPoint(new Vector3(5f, 3f, 5f)).ShouldBeFalse("above it was always air");
    }

    [Fact]
    public void A_notch_under_an_embedded_detail_block_stays_closed()
    {
        // The embedded brush has a coincident same-facing plane on y=0 and its
        // own face there is buried in P. Dropping the cavity wall for that
        // reason leaves no surface on y=0, and the BSP reads the ceiling as empty.
        CsgWorld world = Build(
            Box(new Vector3(0f, -2f, 0f), new Vector3(10f, 2f, 1f)),        // P
            Box(new Vector3(3f, 0f, 0f), new Vector3(7f, 2f, 1f)),          // P', embedded in P
            Negative(new Vector3(4f, -2f, 0f), new Vector3(6f, 0f, 1f)));   // N, a notch in P's underside

        world.ContainsPoint(new Vector3(5f, -1f, 0.5f)).ShouldBeFalse("the notch is open");
        world.ContainsPoint(new Vector3(5f, 0.5f, 0.5f))
            .ShouldBeTrue("the ceiling over the notch is SOLID — this is the fatal case");
        world.ContainsPoint(new Vector3(5f, 1.5f, 0.5f)).ShouldBeTrue();
        world.ContainsPoint(new Vector3(1f, -1f, 0.5f)).ShouldBeTrue("beside the notch is solid");
    }

    [Fact]
    public void Two_overlapping_negatives_cut_their_union()
    {
        CsgWorld world = Build(
            Box(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 2f)),
            Negative(new Vector3(2f, 2f, 0f), new Vector3(6f, 6f, 2f)),
            Negative(new Vector3(4f, 4f, 0f), new Vector3(8f, 8f, 2f)));

        world.ContainsPoint(new Vector3(3f, 3f, 1f)).ShouldBeFalse("first negative");
        world.ContainsPoint(new Vector3(7f, 7f, 1f)).ShouldBeFalse("second negative");
        world.ContainsPoint(new Vector3(5f, 5f, 1f)).ShouldBeFalse("their overlap");
        world.ContainsPoint(new Vector3(9f, 9f, 1f)).ShouldBeTrue("outside both");
        world.ContainsPoint(new Vector3(1f, 1f, 1f)).ShouldBeTrue();
    }

    [Fact]
    public void A_negative_that_swallows_a_brush_annihilates_it()
    {
        // Unordered set model: an additive inside a subtractive cannot be added back.
        CsgWorld world = Build(
            Box(new Vector3(4f, 4f, 4f), new Vector3(6f, 6f, 6f)),
            Negative(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f)));

        world.ContainsPoint(new Vector3(5f, 5f, 5f)).ShouldBeFalse();
        world.Surfaces.Count.ShouldBe(0);
    }

    [Fact]
    public void A_negative_cuts_every_additive_brush_it_overlaps()
    {
        CsgWorld world = Build(
            Box(new Vector3(0f, 0f, 0f), new Vector3(4f, 4f, 4f)),
            Negative(new Vector3(3f, 1f, 1f), new Vector3(5f, 3f, 3f)),
            Box(new Vector3(4f, 0f, 0f), new Vector3(8f, 4f, 4f)));

        world.ContainsPoint(new Vector3(3.5f, 2f, 2f)).ShouldBeFalse("cut out of the first");
        world.ContainsPoint(new Vector3(4.5f, 2f, 2f)).ShouldBeFalse("cut out of the second");
        world.ContainsPoint(new Vector3(1f, 2f, 2f)).ShouldBeTrue();
        world.ContainsPoint(new Vector3(7f, 2f, 2f)).ShouldBeTrue();
    }

    [Fact]
    public void Placement_order_does_not_change_the_solid()
    {
        // A reparent changes traversal order and must not change topology.
        Brush slab = Box(new Vector3(0f, 0f, 0f), new Vector3(10f, 4f, 4f));
        Brush notch = Negative(new Vector3(4f, 0f, 0f), new Vector3(6f, 4f, 4f));

        CsgWorld negativeFirst = Build(notch, slab);
        CsgWorld negativeLast = Build(slab, notch);

        foreach (Vector3 probe in Probes())
        {
            negativeFirst.ContainsPoint(probe)
                .ShouldBe(negativeLast.ContainsPoint(probe), $"order changed the solid at {probe}");
        }
    }

    [Fact]
    public void The_cavity_walls_wear_the_negatives_materials()
    {
        MaterialRef cutMaterial = MaterialRegistry.Intern("Materials/negative_wall.spectramat");
        Brush slab = Box(new Vector3(0f, 0f, 0f), new Vector3(10f, 4f, 4f));
        Brush notch = Brush
            .CreateBox(new Vector3(4f, 1f, 1f), new Vector3(6f, 3f, 3f), cutMaterial)
            .WithOperation(BrushOperation.Subtractive);

        CsgWorld world = Build(slab, notch);

        bool anyWallWearsIt = false;
        foreach (Polygon surface in world.Surfaces)
        {
            if (surface.Face.Material == cutMaterial)
                anyWallWearsIt = true;
        }
        anyWallWearsIt.ShouldBeTrue("no surface carries the negative's material");
    }

    [Fact]
    public void A_world_with_no_negative_compiles_exactly_as_before()
    {
        Brush a = Box(new Vector3(0f, 0f, 0f), new Vector3(4f, 4f, 4f));
        Brush b = Box(new Vector3(2f, 2f, 2f), new Vector3(6f, 6f, 6f));

        CsgWorld world = Build(a, b);

        world.ContainsPoint(new Vector3(1f, 1f, 1f)).ShouldBeTrue();
        world.ContainsPoint(new Vector3(5f, 5f, 5f)).ShouldBeTrue();
        world.ContainsPoint(new Vector3(3f, 3f, 3f)).ShouldBeTrue("the union merged");
        world.ContainsPoint(new Vector3(7f, 7f, 7f)).ShouldBeFalse();
    }

    [Fact]
    public void A_cut_far_from_the_origin_behaves_identically()
    {
        // At 8,000 units float cancellation error is close to the carve epsilons.
        const float O = 8000f;
        CsgWorld world = Build(
            Box(new Vector3(O, 0f, 0f), new Vector3(O + 10f, 8f, 1f)),
            Negative(new Vector3(O + 4f, 0f, 0f), new Vector3(O + 6f, 5f, 1f)));

        world.ContainsPoint(new Vector3(O + 5f, 2f, 0.5f)).ShouldBeFalse("the doorway is open");
        world.ContainsPoint(new Vector3(O + 5f, 7f, 0.5f)).ShouldBeTrue("the lintel is solid");
        world.ContainsPoint(new Vector3(O + 1f, 2f, 0.5f)).ShouldBeTrue();
    }

    private static IEnumerable<Vector3> Probes()
    {
        for (float x = -1f; x <= 11f; x += 1.5f)
            for (float y = -1f; y <= 5f; y += 1.5f)
                for (float z = -1f; z <= 5f; z += 1.5f)
                    yield return new Vector3(x, y, z);
    }

    private static CsgWorld Build(params Brush[] brushes) => CsgWorld.Build(brushes);

    private static Brush Box(float min, float max) =>
        Brush.CreateBox(new Vector3(min, min, min), new Vector3(max, max, max));

    private static Brush Box(Vector3 min, Vector3 max) => Brush.CreateBox(min, max);

    private static Brush Negative(Vector3 min, Vector3 max) =>
        Brush.CreateBox(min, max).WithOperation(BrushOperation.Subtractive);
}
