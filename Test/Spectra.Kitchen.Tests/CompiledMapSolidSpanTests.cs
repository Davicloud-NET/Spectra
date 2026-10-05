using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The span query over a cooked level. It gives the spans and the materials the
/// authored level gives, because the cook keeps what each collision hull face
/// is made of. A map without that table gives the spans and no materials.
/// </summary>
public class CompiledMapSolidSpanTests
{
    // A baked hull's planes went through a file and were normalised once more.
    private const float SameSpan = 1e-4f;

    private const uint UnknownCode = 'Z' | ('Z' << 8) | ('Z' << 16) | ((uint)'Z' << 24);

    [Fact]
    public void A_cooked_level_gives_the_spans_and_materials_of_the_level_it_was_cooked_from()
    {
        using CookedLevel level = CookedLevel.Bake(Room().Scene);

        int solids = 0;
        var materials = new HashSet<MaterialRef>();

        foreach ((Vector3 from, Vector3 to) in RoomSegments())
        {
            foreach (SolidSpan span in SameSpans(level, from, to))
            {
                solids++;
                materials.Add(span.Material);
            }
        }

        // The comparison went through walls, the door, the plain block and the
        // faces that differ on one brush.
        solids.ShouldBeGreaterThan(200);
        materials.ShouldContain(SpanLevel.Brick);
        materials.ShouldContain(SpanLevel.Plaster);
        materials.ShouldContain(SpanLevel.Tile);
        materials.ShouldContain(SpanLevel.Wood);
        materials.ShouldContain(MaterialRef.Default);
    }

    [Fact]
    public void A_baked_hull_carries_the_material_of_every_face_of_its_brush()
    {
        using CookedLevel level = CookedLevel.Bake(Room().Scene);

        SceneNode[] authored = [.. level.Authored.Root.Traverse().Where(node => node.IsStaticWorldBrush)];
        IReadOnlyList<BrushPlacement> hulls = level.Scene.CompiledStaticWorld.ShouldNotBeNull().CollisionPlacements;

        hulls.Count.ShouldBe(authored.Length);

        int faces = 0;
        for (int i = 0; i < authored.Length; i++)
        {
            Brush brush = authored[i].Brush.ShouldNotBeNull();
            hulls[i].Brush.FaceSurfaces.Count.ShouldBe(brush.FaceSurfaces.Count, authored[i].Name);

            for (int plane = 0; plane < brush.FaceSurfaces.Count; plane++, faces++)
            {
                hulls[i].Brush.FaceSurfaces[plane].Material.ShouldBe(
                    brush.FaceSurfaces[plane].Material, $"{authored[i].Name}, plane {plane}");
            }
        }

        faces.ShouldBeGreaterThan(30);
    }

    [Fact]
    public void A_cooked_level_says_nothing_is_missing()
    {
        using CookedLevel level = CookedLevel.Bake(Room().Scene);

        level.Report.CollisionFaceMaterialsMissing.ShouldBeFalse();
        level.Report.IsComplete.ShouldBeTrue(level.Report.Describe());
        level.CarvesDuringLoad.ShouldBe(0);
    }

    [Fact]
    public void A_map_without_the_table_gives_the_same_spans_with_the_default_material_and_the_report_says_why()
    {
        // No parts: a part's material comes from its own brush, table or not.
        using CookedLevel level = CookedLevel.Bake(Room(withParts: false).Scene);

        byte[] file = (byte[])level.File.Clone();
        ScmapSurgery.SetU32(file, ScmapSurgery.TableRecord(file, ScmapFormat.HullMaterialSection), UnknownCode);

        var renderer = new FakeRenderer();
        var bare = new Scene("bare");
        CompiledMapLoadReport report =
            CompiledMapLoader.Load(bare, renderer, ContentBlob.CopyOf(file), "Maps/Room.scmap");

        try
        {
            report.CollisionFaceMaterialsMissing.ShouldBeTrue();
            report.IsComplete.ShouldBeFalse();
            report.Describe().ShouldNotBeNull().ShouldContain("no COLM section");
            report.CollisionHullsLoaded.ShouldBe(level.Report.CollisionHullsLoaded);

            int solids = 0;
            var with = new SolidSpan[32];
            var without = new SolidSpan[32];

            foreach ((Vector3 from, Vector3 to) in RoomSegments())
            {
                int expected = level.Scene.TraceSolidSpans(from, to, with, out _);
                int actual = bare.TraceSolidSpans(from, to, without, out _);

                // With no material to tell them apart, touching spans are one.
                // The solid is the same either way.
                Thickness(without.AsSpan(0, actual)).ShouldBe(
                    Thickness(with.AsSpan(0, expected)), SameSpan, $"from {from} to {to}");

                if (actual > 0)
                {
                    without[0].Start.ShouldBe(with[0].Start, SameSpan);
                    without[actual - 1].End.ShouldBe(with[expected - 1].End, SameSpan);
                }

                for (int i = 0; i < actual; i++, solids++)
                    without[i].Material.ShouldBe(MaterialRef.Default);
            }

            solids.ShouldBeGreaterThan(100);
        }
        finally
        {
            bare.ReleaseCompiledStaticWorld(renderer);
        }
    }

    [Fact]
    public void A_map_with_no_world_brushes_has_no_table_and_nothing_to_report()
    {
        var parts = new SpanLevel();
        parts.Part("Crate", new Vector3(0f, 1f, -2f), new Vector3(0.5f), SpanLevel.Wood);

        using CookedLevel level = CookedLevel.Bake(parts.Scene);

        level.Report.CollisionFaceMaterialsMissing.ShouldBeFalse();
        level.Report.IsComplete.ShouldBeTrue(level.Report.Describe());

        SameSpans(level, new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, -8f))
            .ShouldHaveSingleItem().Material.ShouldBe(SpanLevel.Wood);
    }

    [Fact]
    public void A_door_that_moves_in_a_cooked_level_stops_blocking_its_doorway()
    {
        using CookedLevel level = CookedLevel.Bake(Room().Scene);
        var from = new Vector3(0f, 1f, 0f);
        var to = new Vector3(0f, 1f, -5.5f);

        SameSpans(level, from, to).ShouldHaveSingleItem().Material.ShouldBe(SpanLevel.Wood);

        SceneNode door = level.Scene.Root.Traverse().Single(node => node.Name == "Door");
        door.LocalPosition += new Vector3(2.25f, 0f, 0f);

        var spans = new SolidSpan[4];
        level.Scene.TraceSolidSpans(from, to, spans, out _).ShouldBe(0);
    }

    [Fact]
    public void The_demo_course_cooked_gives_the_spans_it_gives_authored()
    {
        MaterialRef structure = MaterialRegistry.Intern("Materials/span_course_structure.spectramat");
        MaterialRef wall = MaterialRegistry.Intern("Materials/span_course_wall.spectramat");
        MaterialRef accent = MaterialRegistry.Intern("Materials/span_course_accent.spectramat");

        var course = new Scene("Course");
        DemoPlayArea.Build(course, structure, wall, accent);

        using CookedLevel level = CookedLevel.Bake(course);

        // Out of the start room through its shut door, and through the wall
        // beside the door.
        SameSpans(level, new Vector3(124f, 1.25f, 0f), new Vector3(133f, 1.25f, 0f))
            .ShouldHaveSingleItem().Material.ShouldBe(accent);
        SameSpans(level, new Vector3(124f, 1.25f, 2.5f), new Vector3(133f, 1.25f, 2.5f))
            .ShouldHaveSingleItem().Material.ShouldBe(wall);

        var random = new Random(20261005);
        int solids = 0;
        for (int i = 0; i < 300; i++)
            solids += SameSpans(level, CoursePoint(random), CoursePoint(random)).Length;

        solids.ShouldBeGreaterThan(300);
    }

    [Fact]
    public void Along_random_segments_a_point_is_inside_a_span_when_the_baked_world_says_it_is_solid()
    {
        int compared = 0, skipped = 0, solid = 0;

        for (int seed = 1; seed <= 12; seed++)
        {
            using CookedLevel level = CookedLevel.Bake(SolidSpanOracle.BuildLevel(seed));
            CompiledStaticWorld baked = level.Scene.CompiledStaticWorld.ShouldNotBeNull();

            SolidSpanOracle.Tally tally = SolidSpanOracle.Check(level.Scene, baked.ContainsPoint, seed);
            compared += tally.Compared;
            skipped += tally.Skipped;
            solid += tally.Solid;
        }

        string counts = $"compared {compared}, skipped {skipped}, solid {solid}";
        compared.ShouldBeGreaterThan(skipped * 3, counts);
        solid.ShouldBeGreaterThan(compared / 10, counts);
        (compared - solid).ShouldBeGreaterThan(compared / 10, counts);
    }

    [Fact]
    public void A_trace_over_a_cooked_level_carves_nothing_and_allocates_nothing_once_warm()
    {
        using CookedLevel level = CookedLevel.Bake(Room().Scene);
        var spans = new SolidSpan[16];
        var from = new Vector3(-5.5f, 2.5f, 3f);
        var to = new Vector3(4.25f, 0.25f, -7.5f);

        int found = 0;
        for (int i = 0; i < 50; i++)
            found += level.Scene.TraceSolidSpans(from, to, spans, out _);

        found.ShouldBeGreaterThan(0);

        long carves = Csg.CarveInvocationsOnThisThread;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            level.Scene.TraceSolidSpans(from, to, spans, out _);

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0L);
        (Csg.CarveInvocationsOnThisThread - carves).ShouldBe(0L);
    }

    // Traces one segment over the cooked level and the authored one, checks
    // they agree, and returns the spans.
    private static SolidSpan[] SameSpans(CookedLevel level, Vector3 from, Vector3 to)
    {
        var live = new SolidSpan[32];
        var baked = new SolidSpan[32];

        int expected = level.Authored.TraceSolidSpans(from, to, live, out bool liveTruncated);
        int actual = level.Scene.TraceSolidSpans(from, to, baked, out bool bakedTruncated);

        string where = $"from {from} to {to}";
        liveTruncated.ShouldBeFalse(where);
        bakedTruncated.ShouldBeFalse(where);
        actual.ShouldBe(expected, where);

        for (int i = 0; i < expected; i++)
        {
            baked[i].Start.ShouldBe(live[i].Start, SameSpan, $"{where}, span {i}");
            baked[i].End.ShouldBe(live[i].End, SameSpan, $"{where}, span {i}");
            baked[i].Material.ShouldBe(live[i].Material, $"{where}, span {i}");
        }

        return baked[..actual];
    }

    private static float Thickness(ReadOnlySpan<SolidSpan> spans)
    {
        float total = 0f;
        foreach (SolidSpan span in spans) total += span.Thickness;
        return total;
    }

    // The wall with three materials on it and its doorway, a tiled floor, a
    // wooden wall behind, a block that names no material, a ramp turned off
    // every axis, and a door and a trigger as parts.
    private static SpanLevel Room(bool withParts = true)
    {
        var level = new SpanLevel();
        level.Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(6f, 0.5f, 8f), SpanLevel.Tile);
        level.Wall();
        level.Doorway();
        level.Box("Back", new Vector3(0f, 1.5f, -6.5f), new Vector3(6f, 1.5f, 0.5f), SpanLevel.Wood);
        level.Box("Plain", new Vector3(4f, 0.75f, 2f), new Vector3(1f, 0.75f, 1f));

        SceneNode ramp = level.Box("Ramp", new Vector3(-3f, 0.5f, 1f), new Vector3(2f, 0.25f, 1f), SpanLevel.Brick);
        ramp.LocalRotation = Quaternion.CreateFromYawPitchRoll(0.5f, 0.25f, 0.375f);

        if (withParts)
        {
            level.Part("Door", new Vector3(0f, 1.2f, -4.25f), new Vector3(1f, 1.2f, 0.2f), SpanLevel.Wood);

            SceneNode trigger = level.Part("Trigger", new Vector3(0f, 1.2f, -3f), new Vector3(1f, 1.2f, 1f));
            trigger.CanCollide = false;
            trigger.CanQuery = false;
            trigger.IsRendered = false;
        }

        return level;
    }

    // A few chosen lines, then seeded random ones through the room's volume.
    private static IEnumerable<(Vector3 From, Vector3 To)> RoomSegments()
    {
        yield return (new(0f, 1f, 0f), new(0f, 1f, -8f));
        yield return (new(3f, 1f, 0f), new(3f, 1f, -8f));
        yield return (new(3f, 1f, -8f), new(3f, 1f, 0f));
        yield return (new(-3f, 1f, -3.9f), new(3f, 1f, -4.6f));
        yield return (new(-2f, 1f, -4.25f), new(5.5f, 1f, -4.25f));
        yield return (new(2.5f, 2f, -4.25f), new(2.5f, -3f, -4.25f));
        yield return (new(4f, 3f, 2f), new(4f, -2f, 2f));
        yield return (new(-6f, 0.5f, 1f), new(6f, 0.75f, 2f));

        var random = new Random(4711);
        for (int i = 0; i < 250; i++)
            yield return (RoomPoint(random), RoomPoint(random));
    }

    private static Vector3 RoomPoint(Random random) => new(
        (float)(random.NextDouble() * 14.0 - 7.0),
        (float)(random.NextDouble() * 6.0 - 2.0),
        (float)(random.NextDouble() * 18.0 - 9.0));

    private static Vector3 CoursePoint(Random random) => new(
        (float)(random.NextDouble() * 52.0 + 120.0),
        (float)(random.NextDouble() * 8.0 - 3.0),
        (float)(random.NextDouble() * 42.0 - 21.0));
}
