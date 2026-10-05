using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Selection;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The marker that shows an entity with no brush, mesh or light, and picking
/// by it. Such a node is not in the spatial index.
/// </summary>
public sealed class EntityMarkerTests
{
    private static ViewportHarness Fixture()
    {
        var harness = new ViewportHarness();
        harness.Scene.Camera.Position = new Vector3(0f, 0f, 12f);
        harness.Scene.Camera.LookAt(Vector3.Zero);
        return harness;
    }

    private static SceneNode AddEntity(ViewportHarness harness, Vector3 position, string className = "info_player_start")
    {
        var node = new SceneNode(className)
        {
            LocalPosition = position,
            Entity = new EntityData(className),
        };

        harness.Scene.Root.AddChild(node);
        return node;
    }

    [Fact]
    public void A_ray_through_a_point_entitys_marker_finds_it()
    {
        ViewportHarness harness = Fixture();
        SceneNode start = AddEntity(harness, Vector3.Zero);

        Ray3 ray = harness.Scene.Camera.ScreenPointToRay(harness.CenterPixel, harness.ViewportSize);

        EntityMarkerPicking.TryPick(
            harness.Scene, harness.Scene.Camera, in ray, harness.ViewportSize,
            out SceneNode? hit, out float distance).ShouldBeTrue();

        hit.ShouldBeSameAs(start);
        distance.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void A_ray_well_clear_of_every_marker_finds_nothing()
    {
        ViewportHarness harness = Fixture();
        AddEntity(harness, Vector3.Zero);

        Ray3 ray = harness.Scene.Camera.ScreenPointToRay(new Vector2(4f, 4f), harness.ViewportSize);

        EntityMarkerPicking.TryPick(
            harness.Scene, harness.Scene.Camera, in ray, harness.ViewportSize,
            out SceneNode? hit, out _).ShouldBeFalse();

        hit.ShouldBeNull();
    }

    [Fact]
    public void Only_an_entity_with_nothing_else_to_show_it_has_a_marker()
    {
        var bare = new SceneNode("bare") { Entity = new EntityData("logic_relay") };
        var door = new SceneNode("door")
        {
            Entity = new EntityData("func_door"),
            Brush = Brush.CreateBox(-Vector3.One, Vector3.One),
        };
        var prop = new SceneNode("prop")
        {
            Entity = new EntityData("prop"),
            MeshRenderer = new MeshRenderer(BoxMesh.Centred(Vector3.One), new Material(null)),
        };
        var lamp = new SceneNode("lamp")
        {
            Entity = new EntityData("lamp"),
            Light = new Light { Kind = LightKind.Point },
        };

        EntityMarkerPicking.HasMarker(bare).ShouldBeTrue();
        EntityMarkerPicking.HasMarker(door).ShouldBeFalse();
        EntityMarkerPicking.HasMarker(prop).ShouldBeFalse();
        EntityMarkerPicking.HasMarker(lamp).ShouldBeFalse();
        EntityMarkerPicking.HasMarker(new SceneNode("group")).ShouldBeFalse();
    }

    [Fact]
    public void A_press_on_a_marker_selects_the_entity()
    {
        ViewportHarness harness = Fixture();
        SceneNode start = AddEntity(harness, new Vector3(2f, 1f, 0f));
        Vector2 at = harness.WorldToScreen(start.WorldPosition);

        harness.Viewport.ClassifyPress(harness.Frame(at)).ShouldBe(ViewportDragMode.SelectAndMove);

        harness.Press(at);
        harness.Release(at);

        harness.Scene.Selection.Items.ShouldBe([start]);
    }

    [Fact]
    public void A_marker_standing_on_a_floor_is_picked_ahead_of_the_floor()
    {
        // Where a player start goes. Its marker is drawn over the floor, so a
        // press anywhere on it has to take the marker.
        var harness = new ViewportHarness();
        harness.Orbit(Vector3.Zero, 12f, 0.9f, -0.6f);
        SceneNode floor = harness.AddBrush(new Vector3(0f, -4f, 0f), 4f, "Floor");
        SceneNode start = AddEntity(harness, Vector3.Zero);

        Vector2 centre = harness.WorldToScreen(start.WorldPosition);
        float radius = LightPicking.PixelRadius(harness.Scene.Camera, harness.ViewportSize, start.WorldPosition);
        radius.ShouldBeGreaterThan(4f);

        // Below the centre on screen the ray meets the floor first.
        foreach (Vector2 offset in new[] { Vector2.Zero, new Vector2(0f, radius * 0.6f), new Vector2(0f, -radius * 0.6f) })
        {
            harness.Scene.Selection.Clear();
            harness.Press(centre + offset);
            harness.Release(centre + offset);

            harness.Scene.Selection.Items.ShouldBe([start], $"pressed {offset} from the marker's centre");
        }

        harness.Scene.Selection.Clear();
        Vector2 beside = centre + new Vector2(radius * 3f, 0f);
        harness.Press(beside);
        harness.Release(beside);

        harness.Scene.Selection.Items.ShouldBe([floor]);
    }

    [Fact]
    public void The_nearer_of_a_lamp_and_a_marker_under_one_pixel_is_picked()
    {
        // Both project to the same pixel; only their depth differs.
        ViewportHarness harness = Fixture();
        SceneNode marker = AddEntity(harness, new Vector3(0f, 0f, 2f));
        var lamp = new SceneNode("Lamp")
        {
            LocalPosition = new Vector3(0f, 0f, -6f),
            Light = new Light { Kind = LightKind.Point, Range = 6f },
        };
        harness.Scene.Root.AddChild(lamp);

        harness.Press(harness.CenterPixel);
        harness.Release(harness.CenterPixel);
        harness.Scene.Selection.Items.ShouldBe([marker]);

        marker.LocalPosition = new Vector3(0f, 0f, -9f);
        harness.Scene.Selection.Clear();

        harness.Press(harness.CenterPixel);
        harness.Release(harness.CenterPixel);
        harness.Scene.Selection.Items.ShouldBe([lamp]);
    }

    [Fact]
    public void A_marquee_takes_each_entity_once_however_it_is_shown()
    {
        ViewportHarness harness = Fixture();
        SceneNode bare = AddEntity(harness, new Vector3(-3f, 0f, 0f));
        SceneNode door = AddEntity(harness, new Vector3(0f, 0f, 0f), "func_door");
        door.Brush = Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));
        SceneNode lamp = AddEntity(harness, new Vector3(3f, 0f, 0f), "lamp");
        lamp.Light = new Light { Kind = LightKind.Point, Range = 4f };

        var rect = ScreenRect.FromCorners(Vector2.Zero, harness.ViewportSize);
        var actual = new List<SceneNode>();
        BoxSelectQuery.Query(harness.Scene, in rect, harness.ViewportSize, BoxSelectMode.Intersect, actual);

        actual.Count.ShouldBe(3);
        actual.ShouldContain(bare);
        actual.ShouldContain(door);
        actual.ShouldContain(lamp);
    }

    [Fact]
    public void A_marquee_that_misses_a_marker_does_not_take_it()
    {
        ViewportHarness harness = Fixture();
        AddEntity(harness, new Vector3(-3f, 0f, 0f));
        SceneNode right = AddEntity(harness, new Vector3(3f, 0f, 0f));

        var rect = ScreenRect.FromCorners(
            new Vector2(harness.ViewportSize.X * 0.5f, 0f), harness.ViewportSize);
        var actual = new List<SceneNode>();
        BoxSelectQuery.Query(harness.Scene, in rect, harness.ViewportSize, BoxSelectMode.Intersect, actual);

        actual.ShouldBe([right]);
    }

    [Fact]
    public void A_point_entity_is_drawn_and_an_entity_with_geometry_is_not()
    {
        ViewportHarness harness = Fixture();
        AddEntity(harness, new Vector3(-3f, 0f, 0f));
        SceneNode door = AddEntity(harness, new Vector3(3f, 0f, 0f), "func_door");
        door.Brush = Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));
        var overlay = new EntityMarkerOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);

        overlay.DrawnLastDraw.ShouldBe(1);
        CountOf(output, EntityMarkerOverlay.DefaultColor).ShouldBeGreaterThan(0);

        foreach (Vector3 vertex in Coloured(output, EntityMarkerOverlay.DefaultColor))
            vertex.X.ShouldBeLessThan(0f);
    }

    [Fact]
    public void Every_stroke_has_a_backing_line_and_they_all_go_down_before_the_colour()
    {
        ViewportHarness harness = Fixture();
        AddEntity(harness, Vector3.Zero);
        var output = new DebugDraw();

        new EntityMarkerOverlay().Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);

        int coloured = CountOf(output, EntityMarkerOverlay.DefaultColor);
        coloured.ShouldBe(output.VertexCount / 2);

        ReadOnlySpan<float> data = output.Vertices;
        for (int vertex = 0; vertex < output.VertexCount; vertex++)
        {
            var colour = new Vector3(data[(vertex * 6) + 3], data[(vertex * 6) + 4], data[(vertex * 6) + 5]);
            bool isColour = colour == EntityMarkerOverlay.DefaultColor;
            isColour.ShouldBe(vertex >= output.VertexCount / 2, $"vertex {vertex}");
        }
    }

    [Fact]
    public void The_arrow_points_along_the_nodes_local_z()
    {
        ViewportHarness harness = Fixture();
        SceneNode start = AddEntity(harness, Vector3.Zero);
        var overlay = new EntityMarkerOverlay();

        foreach (Vector3 facing in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, new Vector3(1f, 0f, 1f) })
        {
            Vector3 forward = Vector3.Normalize(facing);
            start.LocalRotation = Light.RotationForDirection(forward);
            var output = new DebugDraw();

            overlay.Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);

            Vector3 tip = Farthest(output, EntityMarkerOverlay.DefaultColor, start.WorldPosition);
            Vector3.Dot(Vector3.Normalize(tip - start.WorldPosition), forward)
                .ShouldBeGreaterThan(0.99f, $"facing {facing}");
        }
    }

    [Fact]
    public void A_logic_entity_gets_a_marker_with_no_arrow()
    {
        // Its transform means nothing, so an arrow would say something false.
        ViewportHarness harness = Fixture();
        harness.Scene.EntitySchemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
        [
            new EntitySchema("logic_relay", placement: EntityPlacement.Abstract),
            new EntitySchema("info_player_start"),
        ]));
        SceneNode relay = AddEntity(harness, new Vector3(-3f, 0f, 0f), "logic_relay");
        SceneNode start = AddEntity(harness, new Vector3(3f, 0f, 0f), "info_player_start");
        SceneNode unknown = AddEntity(harness, new Vector3(0f, 3f, 0f), "from_another_game");
        var overlay = new EntityMarkerOverlay();

        Reach(harness, overlay, relay).ShouldBeLessThan(1f);
        Reach(harness, overlay, start).ShouldBeGreaterThan(2f);
        Reach(harness, overlay, unknown).ShouldBeGreaterThan(2f);
    }

    [Fact]
    public void A_far_marker_is_a_small_diamond_and_nothing_else()
    {
        ViewportHarness harness = Fixture();
        SceneNode start = AddEntity(harness, new Vector3(0f, 0f, -400f));
        var output = new DebugDraw();

        new EntityMarkerOverlay().Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);

        LightPicking.PixelRadius(harness.Scene.Camera, harness.ViewportSize, start.WorldPosition)
            .ShouldBe(LightOverlay.MinIconPixels, tolerance: 0.05f);

        // One backing diamond and two in colour, four hairlines each.
        output.VertexCount.ShouldBe(3 * 4 * 2);
        CountOf(output, EntityMarkerOverlay.DefaultColor).ShouldBe(2 * 4 * 2);
    }

    [Fact]
    public void The_marker_has_its_own_colour()
    {
        EntityMarkerOverlay.DefaultColor.ShouldNotBe(PartBrushOverlay.DefaultColor);
        EntityMarkerOverlay.DefaultColor.ShouldNotBe(SubtractiveBrushOverlay.DefaultColor);
        EntityMarkerOverlay.DefaultColor.ShouldNotBe(VolumeOverlay.DefaultColor);
        EntityMarkerOverlay.DefaultColor.ShouldNotBe(SelectionOutline.SelectedColor);
    }

    [Fact]
    public void The_budget_is_disclosed_rather_than_silently_truncating()
    {
        ViewportHarness harness = Fixture();
        for (int i = 0; i < 10; i++)
            AddEntity(harness, new Vector3(i - 5f, 0f, 0f));
        var overlay = new EntityMarkerOverlay { MaxMarkers = 4 };

        overlay.Draw(new DebugDraw(), harness.Scene, harness.Scene.Camera, harness.ViewportSize);

        overlay.DrawnLastDraw.ShouldBe(4);
        overlay.SkippedLastDraw.ShouldBe(6);
    }

    [Fact]
    public void A_disabled_overlay_draws_nothing()
    {
        ViewportHarness harness = Fixture();
        AddEntity(harness, Vector3.Zero);
        var overlay = new EntityMarkerOverlay { Enabled = false };
        var output = new DebugDraw();

        overlay.Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);

        output.VertexCount.ShouldBe(0);
        overlay.DrawnLastDraw.ShouldBe(0);
    }

    [Fact]
    public void Drawing_and_picking_markers_allocates_nothing_per_frame()
    {
        ViewportHarness harness = Fixture();
        for (int i = 0; i < 40; i++)
            AddEntity(harness, new Vector3((i % 8) - 4f, (i / 8) - 2f, 0f));
        var overlay = new EntityMarkerOverlay();
        var output = new DebugDraw();
        Ray3 ray = harness.Scene.Camera.ScreenPointToRay(harness.CenterPixel, harness.ViewportSize);

        for (int i = 0; i < 200; i++)
            Frame(harness, overlay, output, in ray);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2_000; i++)
            Frame(harness, overlay, output, in ray);
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0);

        static void Frame(ViewportHarness harness, EntityMarkerOverlay overlay, DebugDraw output, in Ray3 ray)
        {
            output.Clear();
            overlay.Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);
            EntityMarkerPicking.TryPick(
                harness.Scene, harness.Scene.Camera, in ray, harness.ViewportSize, out _, out _);
        }
    }

    [Fact]
    public void The_editor_draws_a_marker_for_a_point_entity()
    {
        var scene = new Scene("Editor");
        var renderer = new CompilingRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));
        var host = new SceneEditorHost(
            NullLoggerFactory.Instance, scene, renderer,
            new InputManager(NullLogger<InputManager>.Instance));
        scene.Camera.Position = new Vector3(0f, 0f, 12f);
        scene.Camera.LookAt(Vector3.Zero);

        var empty = new DebugDraw();
        host.Draw(empty);
        CountOf(empty, EntityMarkerOverlay.DefaultColor).ShouldBe(0);

        scene.Root.AddChild(new SceneNode("start") { Entity = new EntityData("info_player_start") });

        var marked = new DebugDraw();
        host.Draw(marked);
        CountOf(marked, EntityMarkerOverlay.DefaultColor).ShouldBeGreaterThan(0);
    }

    // How far the marker's colour reaches from the node, in marker radii.
    private static float Reach(ViewportHarness harness, EntityMarkerOverlay overlay, SceneNode node)
    {
        var output = new DebugDraw();
        overlay.Draw(output, harness.Scene, harness.Scene.Camera, harness.ViewportSize);

        Vector3 at = node.WorldPosition;
        float radius = LightPicking.WorldRadius(harness.Scene.Camera, harness.ViewportSize, at);
        float reach = 0f;

        // Others' markers are further off than any arrow reaches.
        foreach (Vector3 vertex in Coloured(output, EntityMarkerOverlay.DefaultColor))
        {
            float distance = Vector3.Distance(vertex, at);
            if (distance < radius * 3f)
                reach = MathF.Max(reach, distance);
        }

        return reach / radius;
    }

    private static Vector3 Farthest(DebugDraw output, Vector3 color, Vector3 from)
    {
        Vector3 farthest = from;
        foreach (Vector3 vertex in Coloured(output, color))
        {
            if (Vector3.DistanceSquared(vertex, from) > Vector3.DistanceSquared(farthest, from))
                farthest = vertex;
        }

        return farthest;
    }

    // Six floats per vertex: position, then colour.
    private static List<Vector3> Coloured(DebugDraw output, Vector3 color)
    {
        ReadOnlySpan<float> data = output.Vertices;
        var positions = new List<Vector3>();
        for (int i = 0; i + 5 < data.Length; i += 6)
        {
            if (new Vector3(data[i + 3], data[i + 4], data[i + 5]) == color)
                positions.Add(new Vector3(data[i], data[i + 1], data[i + 2]));
        }

        return positions;
    }

    private static int CountOf(DebugDraw output, Vector3 color) => Coloured(output, color).Count;
}
