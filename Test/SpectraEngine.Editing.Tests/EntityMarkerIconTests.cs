using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// A point entity's marker is its class's icon file, in its group's colour.
/// </summary>
// The tests ask the glyph table what a file draws. None of them knows a
// drawing, so swapping an icon file changes no test.
public sealed class EntityMarkerIconTests
{
    private const string Sound = "test_sound";
    private const string Relay = "test_relay";
    private const string Plain = "test_plain";
    private const string Wrong = "test_wrong";

    // A stroke is two lines in colour, and an arrow is three strokes.
    private const int LinesAStroke = 2;
    private const int ArrowStrokes = 3;

    // Classes that exist only as .sentdef bytes, the way the editor learns any class.
    private static readonly EntitySchemaCatalog Schemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema(Sound, group: "Sound", icon: "sound"),
        new EntitySchema(Relay, group: "Logic", placement: EntityPlacement.Abstract, icon: "relay"),
        new EntitySchema(Plain, group: "Player"),
        new EntitySchema(Wrong, group: "Triggers", icon: "no-such-drawing"),
    ]));

    private static ViewportHarness Fixture()
    {
        var harness = new ViewportHarness();
        harness.Scene.EntitySchemas = Schemas;
        harness.Scene.Camera.Position = new Vector3(0f, 0f, 12f);
        harness.Scene.Camera.LookAt(Vector3.Zero);
        return harness;
    }

    private static SceneNode Add(ViewportHarness harness, string className, Vector3 position)
    {
        var node = new SceneNode(className) { LocalPosition = position, Entity = new EntityData(className) };
        harness.Scene.Root.AddChild(node);
        return node;
    }

    private static DebugDraw Draw(ViewportHarness harness, EntityMarkerOverlay? overlay = null)
    {
        var output = new DebugDraw();
        (overlay ?? new EntityMarkerOverlay()).Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);
        return output;
    }

    private static ViewportGlyph Glyph(string key)
    {
        ViewportGlyphs.TryGet(key, out ViewportGlyph? glyph).ShouldBeTrue(key);
        return glyph.ShouldNotBeNull();
    }

    [Fact]
    public void A_marker_is_its_classs_icon_in_its_groups_colour()
    {
        ViewportHarness harness = Fixture();
        Add(harness, Sound, Vector3.Zero);

        DebugDraw output = Draw(harness);

        // As many backing lines again, and nothing else.
        int strokes = Glyph("IconEntitySound").SegmentCount + ArrowStrokes;
        DebugLines.CountOf(output, EntityGroupTints.For("Sound")).ShouldBe(strokes * LinesAStroke);
        output.VertexCount.ShouldBe(strokes * LinesAStroke * 2 * 2);
    }

    [Fact]
    public void A_sound_and_a_relay_draw_different_strokes()
    {
        ViewportHarness sounds = Fixture();
        Add(sounds, Sound, Vector3.Zero);
        ViewportHarness relays = Fixture();
        Add(relays, Relay, Vector3.Zero);

        List<Vector3> sound = DebugLines.Coloured(Draw(sounds), EntityGroupTints.For("Sound"));
        List<Vector3> relay = DebugLines.Coloured(Draw(relays), EntityGroupTints.For("Logic"));

        sound.ShouldNotBeEmpty();
        relay.ShouldNotBeEmpty();
        sound.ShouldNotBe(relay);
    }

    [Fact]
    public void A_logic_class_gets_its_icon_and_no_arrow()
    {
        // Its transform means nothing, so an arrow would say something false.
        ViewportHarness harness = Fixture();
        Add(harness, Relay, Vector3.Zero);

        DebugDraw output = Draw(harness);

        DebugLines.CountOf(output, EntityGroupTints.For("Logic"))
            .ShouldBe(Glyph("IconEntityRelay").SegmentCount * LinesAStroke);
    }

    [Theory]
    [InlineData(Plain, "Player")]
    [InlineData(Wrong, "Triggers")]
    public void A_class_with_no_icon_or_a_wrong_one_draws_the_plain_entity_glyph(string className, string group)
    {
        ViewportHarness harness = Fixture();
        Add(harness, className, Vector3.Zero);

        DebugDraw output = Draw(harness);

        DebugLines.CountOf(output, EntityGroupTints.For(group))
            .ShouldBe((Glyph(WorldIcons.Entity).SegmentCount + ArrowStrokes) * LinesAStroke);
    }

    [Fact]
    public void A_class_no_schema_knows_draws_the_unknown_glyph_in_the_plain_colour()
    {
        ViewportHarness harness = Fixture();
        Add(harness, "from_another_game", Vector3.Zero);

        DebugDraw output = Draw(harness);

        DebugLines.CountOf(output, EntityGroupTints.For(null))
            .ShouldBe((Glyph(WorldIcons.UnknownEntity).SegmentCount + ArrowStrokes) * LinesAStroke);
    }

    [Fact]
    public void A_far_marker_is_a_diamond_whatever_its_class()
    {
        ViewportHarness harness = Fixture();
        Add(harness, Sound, new Vector3(0f, 0f, -400f));
        var overlay = new EntityMarkerOverlay();

        DebugDraw output = Draw(harness, overlay);

        // One backing diamond and two in colour, four hairlines each.
        output.VertexCount.ShouldBe(3 * 4 * 2);
        DebugLines.CountOf(output, EntityGroupTints.For("Sound")).ShouldBe(2 * 4);
        overlay.DrawnLastDraw.ShouldBe(1);
        overlay.IconsLastDraw.ShouldBe(0);
    }

    [Fact]
    public void A_smaller_icon_is_drawn_with_strokes_one_pixel_wide()
    {
        // Two lines of colour a stroke would run together in a drawing this small.
        ViewportHarness harness = Fixture();
        SceneNode relay = Add(harness, Relay, new Vector3(0f, 0f, -14f));
        var overlay = new EntityMarkerOverlay();

        DebugDraw output = Draw(harness, overlay);

        float pixels = LightPicking.PixelRadius(harness.Scene.Camera, harness.ViewportSize, relay.WorldPosition);
        pixels.ShouldBeInRange(7f, 10f);
        overlay.IconsLastDraw.ShouldBe(1);

        // One line of colour between two of backing.
        int segments = Glyph("IconEntityRelay").SegmentCount;
        DebugLines.CountOf(output, EntityGroupTints.For("Logic")).ShouldBe(segments);
        output.VertexCount.ShouldBe(segments * 3 * 2);
    }

    [Fact]
    public void The_icon_faces_the_camera()
    {
        ViewportHarness harness = Fixture();
        harness.Orbit(Vector3.Zero, 12f, 0.9f, -0.6f);
        SceneNode relay = Add(harness, Relay, Vector3.Zero);

        List<Vector3> drawn = DebugLines.Coloured(Draw(harness), EntityGroupTints.For("Logic"));

        drawn.ShouldNotBeEmpty();
        foreach (Vector3 vertex in drawn)
            Vector3.Dot(vertex - relay.WorldPosition, harness.Scene.Camera.Forward).ShouldBe(0f, tolerance: 1e-3f);
    }

    [Fact]
    public void The_icon_is_drawn_the_way_up_its_file_is()
    {
        // The middle of what is drawn is the middle of the file's strokes:
        // right of centre in the file is right of centre on screen, and up is up.
        ViewportHarness harness = Fixture();
        SceneNode relay = Add(harness, Relay, new Vector3(1f, 2f, 0f));
        Camera camera = harness.Scene.Camera;

        List<Vector3> drawn = DebugLines.Coloured(Draw(harness), EntityGroupTints.For("Logic"));

        Vector3 middle = Vector3.Zero;
        foreach (Vector3 vertex in drawn)
            middle += vertex / drawn.Count;

        ViewportGlyph glyph = Glyph("IconEntityRelay");
        Vector2 expected = Vector2.Zero;
        for (int stroke = 0; stroke < glyph.StrokeCount; stroke++)
        {
            ReadOnlySpan<Vector2> points = glyph.Stroke(stroke);
            for (int i = 1; i < points.Length; i++)
                expected += (points[i - 1] + points[i]) * 0.5f / glyph.SegmentCount;
        }

        float radius = LightPicking.WorldRadius(camera, harness.ViewportSize, relay.WorldPosition);
        float size = radius * ViewportGlyphs.Reach * 2f;
        Vector3 offset = middle - relay.WorldPosition;

        Vector3.Dot(offset, camera.Right).ShouldBe((expected.X - 0.5f) * size, tolerance: size * 1e-3f);
        Vector3.Dot(offset, camera.Up).ShouldBe((0.5f - expected.Y) * size, tolerance: size * 1e-3f);
    }

    [Fact]
    public void When_more_markers_are_near_than_may_be_icons_the_nearest_get_the_icon()
    {
        ViewportHarness harness = Fixture();
        var nodes = new List<SceneNode>();

        // Listed far to near, so scene order is not what picks them.
        for (int i = 5; i >= 0; i--)
            nodes.Add(Add(harness, Relay, new Vector3((i - 2.5f) * 2f, 0f, -i)));

        var overlay = new EntityMarkerOverlay { MaxIcons = 2 };
        List<Vector3> drawn = DebugLines.Coloured(Draw(harness, overlay), EntityGroupTints.For("Logic"));

        overlay.DrawnLastDraw.ShouldBe(6);
        overlay.IconsLastDraw.ShouldBe(2);

        int iconVertices = Glyph("IconEntityRelay").SegmentCount * LinesAStroke * 2;
        const int DiamondVertices = 2 * 4 * 2;

        for (int i = 0; i < nodes.Count; i++)
        {
            bool isNearest = i >= nodes.Count - 2;
            drawn.Count(vertex => Vector3.Distance(vertex, nodes[i].WorldPosition) < 0.9f)
                .ShouldBe(isNearest ? iconVertices : DiamondVertices, $"marker {i}");
        }
    }

    [Fact]
    public void With_room_for_them_every_near_marker_is_an_icon()
    {
        ViewportHarness harness = Fixture();
        for (int i = 0; i < 6; i++)
            Add(harness, Relay, new Vector3((i - 2.5f) * 2f, 0f, 0f));
        var overlay = new EntityMarkerOverlay();

        Draw(harness, overlay);

        overlay.DrawnLastDraw.ShouldBe(6);
        overlay.IconsLastDraw.ShouldBe(6);
    }

    [Fact]
    public void A_second_draw_allocates_nothing()
    {
        ViewportHarness harness = Fixture();
        string[] classes = [Sound, Relay, Plain, Wrong, "from_another_game"];
        for (int i = 0; i < 40; i++)
            Add(harness, classes[i % classes.Length], new Vector3((i % 8) - 4f, (i / 8) - 2f, 0f));
        var overlay = new EntityMarkerOverlay { MaxIcons = 16 };
        var output = new DebugDraw();

        overlay.Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++)
        {
            output.Clear();
            overlay.Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0);
        overlay.IconsLastDraw.ShouldBe(16);
    }
}
