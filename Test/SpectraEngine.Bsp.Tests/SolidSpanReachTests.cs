using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The span query over distance: brushes that reach across cells, segments
/// that cross many of them, levels far from the origin, and what a call costs
/// in allocations.
/// </summary>
public sealed class SolidSpanReachTests
{
    private const float Exact = 1e-4f;

    // What a level 100,000 units out may differ by from the same level at the
    // origin. A float there resolves 1/128 of a unit, so the level and the
    // segments below sit on that grid.
    private const float FarTolerance = 1e-3f;

    [Fact]
    public void A_brush_that_spans_several_cells_counts_once()
    {
        var level = new SpanLevel();
        level.Box("Slab", new Vector3(0f, -0.5f, 0f), new Vector3(100f, 0.5f, 100f), SpanLevel.Tile);
        level.Compile();

        level.Scene.StaticWorld.ShouldNotBeNull().Chunks.Count.ShouldBeGreaterThan(30);

        // Along the inside of the slab, across six cells of it.
        SolidSpan lengthwise = level.Trace(new Vector3(-90f, -0.5f, 3f), new Vector3(90f, -0.5f, 3f))
            .ShouldHaveSingleItem();
        lengthwise.Start.ShouldBe(0f);
        lengthwise.End.ShouldBe(180f);

        // Met in every one of those cells and clipped once.
        level.Scene.LastSolidSpanBrushCount.ShouldBe(1);

        // Down through it where four cells meet.
        SolidSpan across = level.Trace(new Vector3(0f, 5f, 0f), new Vector3(0f, -5f, 0f)).ShouldHaveSingleItem();
        across.Start.ShouldBe(5f, Exact);
        across.End.ShouldBe(6f, Exact);
    }

    [Fact]
    public void A_segment_that_crosses_many_cells_finds_a_wall_in_the_last_one()
    {
        var level = new SpanLevel();
        level.Box("Near", new Vector3(2f, 1f, 0f), new Vector3(0.5f, 1f, 1f), SpanLevel.Brick);
        level.Box("Far", new Vector3(300.5f, 1f, 9f), new Vector3(0.5f, 1f, 30f), SpanLevel.Wood);
        level.Compile();

        var from = new Vector3(0f, 1f, 0f);
        var to = new Vector3(310f, 1f, 18f);
        float perUnitOfX = Vector3.Distance(from, to) / 310f;

        SolidSpan[] spans = level.Trace(from, to);

        spans.Length.ShouldBe(2);
        level.Scene.LastSolidSpanBrushCount.ShouldBe(2);
        spans[0].Material.ShouldBe(SpanLevel.Brick);
        spans[1].Start.ShouldBe(300f * perUnitOfX, 1e-3f);
        spans[1].End.ShouldBe(301f * perUnitOfX, 1e-3f);
        spans[1].Material.ShouldBe(SpanLevel.Wood);
    }

    [Fact]
    public void A_segment_through_empty_cells_between_two_islands_finds_both()
    {
        var level = new SpanLevel();
        level.Box("West", new Vector3(-500f, 1f, -500f), new Vector3(1f, 1f, 1f), SpanLevel.Brick);
        level.Box("East", new Vector3(500f, 1f, 500f), new Vector3(1f, 1f, 1f), SpanLevel.Wood);
        level.Compile();

        SolidSpan[] spans = level.Trace(new Vector3(-600f, 1f, -600f), new Vector3(600f, 1f, 600f));

        spans.Length.ShouldBe(2);
        spans[0].Material.ShouldBe(SpanLevel.Brick);
        spans[1].Material.ShouldBe(SpanLevel.Wood);
        spans[0].Thickness.ShouldBe(2f * MathF.Sqrt(2f), 1e-3f);
        spans[1].Thickness.ShouldBe(2f * MathF.Sqrt(2f), 1e-3f);
    }

    [Theory]
    [InlineData(100_000f, 0f, 0f)]
    [InlineData(0f, 0f, -100_000f)]
    [InlineData(100_000f, 4_096f, 100_000f)]
    public void A_level_far_from_the_origin_gives_the_thicknesses_it_gives_at_the_origin(float x, float y, float z)
    {
        SpanLevel near = FarLevel(Vector3.Zero);
        SpanLevel far = FarLevel(new Vector3(x, y, z));

        (Vector3 From, Vector3 To)[] segments =
        [
            (new(0f, 1f, 0f), new(0f, 1f, -8f)),
            (new(3f, 1f, 0f), new(3f, 1f, -8f)),
            (new(-3f, 1f, -3.875f), new(3f, 1f, -4.625f)),
            (new(-5.5f, 2.5f, 3f), new(4.25f, 0.25f, -7.5f)),
            (new(2.5f, 2f, -4.25f), new(2.5f, -3f, -4.25f)),
            (new(-1.5f, 1f, -6f), new(1.5f, 1.125f, 2f)),
        ];

        int solids = 0;
        foreach ((Vector3 from, Vector3 to) in segments)
        {
            SolidSpan[] expected = near.Trace(from, to);
            SolidSpan[] actual = far.Trace(from, to);

            actual.Length.ShouldBe(expected.Length, $"from {from} to {to}");
            for (int i = 0; i < expected.Length; i++)
            {
                actual[i].Start.ShouldBe(expected[i].Start, FarTolerance, $"from {from} to {to}, span {i}");
                actual[i].End.ShouldBe(expected[i].End, FarTolerance, $"from {from} to {to}, span {i}");
                actual[i].Material.ShouldBe(expected[i].Material);
            }

            solids += expected.Length;
        }

        // The segments went through something, so the comparison compared something.
        solids.ShouldBeGreaterThan(6);
    }

    [Fact]
    public void A_second_trace_on_the_same_scene_allocates_nothing()
    {
        SpanLevel level = FarLevel(Vector3.Zero);
        var filter = new SceneQueryFilter { IgnoreQueryFlags = true };
        var spans = new SolidSpan[16];

        var across = (From: new Vector3(-5.5f, 2.5f, 3f), To: new Vector3(4.25f, 0.25f, -7.5f));
        var along = (From: new Vector3(-40f, 1f, -4.25f), To: new Vector3(40f, 1f, -4.3f));

        // Warm-up: grows the scratch storage and lets the JIT settle.
        int found = 0;
        for (int i = 0; i < 50; i++)
        {
            found += level.Scene.TraceSolidSpans(across.From, across.To, spans, out _);
            found += level.Scene.TraceSolidSpans(along.From, along.To, in filter, spans, out _);
        }

        found.ShouldBeGreaterThan(100);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            level.Scene.TraceSolidSpans(across.From, across.To, spans, out _);
            level.Scene.TraceSolidSpans(along.From, along.To, in filter, spans, out _);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0L);
    }

    // A room with a doorway, a door standing in it, a second wall behind and a
    // ramp turned off every axis, so the far copy has rotation to get wrong.
    private static SpanLevel FarLevel(Vector3 origin)
    {
        var level = new SpanLevel(origin);
        level.Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(6f, 0.5f, 8f), SpanLevel.Tile);
        level.Wall();
        level.Doorway();
        level.Box("Back", new Vector3(0f, 1.5f, -6.5f), new Vector3(6f, 1.5f, 0.5f), SpanLevel.Wood);

        SceneNode ramp = level.Box("Ramp", new Vector3(-3f, 0.5f, 1f), new Vector3(2f, 0.25f, 1f), SpanLevel.Brick);
        ramp.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.5f, 0.25f, 0.375f);

        level.Compile();

        level.Part("Door", new Vector3(0.5f, 1.25f, -4.25f), new Vector3(1f, 1.25f, 0.125f), SpanLevel.Wood);
        return level;
    }
}
