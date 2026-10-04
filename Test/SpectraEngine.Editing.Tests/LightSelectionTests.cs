using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Selection;
using SpectraEngine.Editing.Viewport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Picking and marquee-selecting lights by their viewport icon. Lights are not
/// in the spatial index.
/// </summary>
public sealed class LightSelectionTests
{
    private static ViewportHarness BuildLitScene(params Vector3[] positions)
    {
        var harness = new ViewportHarness();

        for (int i = 0; i < positions.Length; i++)
        {
            var node = new SceneNode($"Lamp{i}")
            {
                LocalPosition = positions[i],
                Light = new Light { Kind = LightKind.Point, Range = 6f },
            };

            harness.Scene.Root.AddChild(node);
        }

        harness.Scene.Camera.Position = new Vector3(0f, 0f, 12f);
        harness.Scene.Camera.LookAt(Vector3.Zero);
        return harness;
    }

    [Fact]
    public void A_ray_through_a_lamps_icon_finds_it()
    {
        ViewportHarness harness = BuildLitScene(Vector3.Zero);
        SceneNode lamp = harness.Scene.Root.Children[0];

        Ray3 ray = harness.Scene.Camera.ScreenPointToRay(harness.CenterPixel, harness.ViewportSize);

        LightPicking.TryPick(
            harness.Scene, harness.Scene.Camera, in ray, harness.ViewportSize,
            out SceneNode? hit, out float distance).ShouldBeTrue();

        hit.ShouldBeSameAs(lamp);
        distance.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void A_ray_well_clear_of_every_icon_finds_nothing()
    {
        ViewportHarness harness = BuildLitScene(Vector3.Zero);

        Ray3 ray = harness.Scene.Camera.ScreenPointToRay(
            new Vector2(4f, 4f), harness.ViewportSize);

        LightPicking.TryPick(
            harness.Scene, harness.Scene.Camera, in ray, harness.ViewportSize,
            out SceneNode? hit, out _).ShouldBeFalse();

        hit.ShouldBeNull();
    }

    [Fact]
    public void The_nearest_of_two_stacked_lamps_wins()
    {
        // Both project to the same pixel; only their depth differs.
        ViewportHarness harness = BuildLitScene(
            new Vector3(0f, 0f, -6f),
            new Vector3(0f, 0f, 2f));

        SceneNode near = harness.Scene.Root.Children[1];

        Ray3 ray = harness.Scene.Camera.ScreenPointToRay(harness.CenterPixel, harness.ViewportSize);

        LightPicking.TryPick(
            harness.Scene, harness.Scene.Camera, in ray, harness.ViewportSize,
            out SceneNode? hit, out _).ShouldBeTrue();

        hit.ShouldBeSameAs(near);
    }

    [Fact]
    public void The_icon_is_one_size_in_the_world_between_its_two_screen_limits()
    {
        // Held to a constant screen size, an icon grows against the level as
        // the camera pulls back. It is a fixed world size instead, capped up
        // close and floored far away so it never fills the view or vanishes.
        ViewportHarness harness = BuildLitScene(Vector3.Zero);
        Camera camera = harness.Scene.Camera;
        Vector2 viewport = harness.ViewportSize;

        Vector3 At(float distance) => camera.Position + (camera.Forward * distance);

        // The distance at which the world size comes out as ten pixels.
        float perPixelAtOne = GizmoMath.WorldPerPixel(camera, viewport.Y, 1f);
        float middle = LightOverlay.IconWorldRadius / (10f * perPixelAtOne);

        LightPicking.WorldRadius(camera, viewport, At(middle))
            .ShouldBe(LightOverlay.IconWorldRadius, tolerance: 1e-4f);
        LightPicking.PixelRadius(camera, viewport, At(middle)).ShouldBe(10f, tolerance: 0.05f);

        LightPicking.PixelRadius(camera, viewport, At(middle * 0.05f))
            .ShouldBe(LightOverlay.IconPixels, tolerance: 0.05f);
        LightPicking.PixelRadius(camera, viewport, At(middle * 50f))
            .ShouldBe(LightOverlay.MinIconPixels, tolerance: 0.05f);

        // Twice as far is half as big on screen, which is what a constant
        // screen size got wrong.
        LightPicking.PixelRadius(camera, viewport, At(middle * 1.2f))
            .ShouldBe(10f / 1.2f, tolerance: 0.05f);
    }

    [Fact]
    public void A_lamp_behind_the_camera_is_never_picked()
    {
        ViewportHarness harness = BuildLitScene(new Vector3(0f, 0f, 40f));

        Ray3 ray = harness.Scene.Camera.ScreenPointToRay(harness.CenterPixel, harness.ViewportSize);

        LightPicking.TryPick(
            harness.Scene, harness.Scene.Camera, in ray, harness.ViewportSize,
            out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(BoxSelectMode.Intersect)]
    [InlineData(BoxSelectMode.Contain)]
    public void A_marquee_picks_exactly_the_lamps_the_oracle_says_it_should(BoxSelectMode mode)
    {
        ViewportHarness harness = BuildLitScene(
            new Vector3(-4f, 2f, 0f),
            new Vector3(0f, 0f, 0f),
            new Vector3(4f, -2f, 0f),
            new Vector3(-1f, -3f, 0f));

        // Cuts through the field so Contain and Intersect disagree.
        var rect = ScreenRect.FromCorners(new Vector2(220f, 180f), new Vector2(560f, 430f));

        var actual = new List<SceneNode>();
        BoxSelectQuery.Query(harness.Scene, in rect, harness.ViewportSize, mode, actual);

        SceneNode[] expected = Oracle(harness, in rect, mode);

        actual.Select(n => n.Name).OrderBy(n => n)
            .ShouldBe(expected.Select(n => n.Name).OrderBy(n => n));
    }

    [Fact]
    public void A_marquee_across_a_room_of_lamps_no_longer_selects_nothing()
    {
        ViewportHarness harness = BuildLitScene(
            new Vector3(-3f, 0f, 0f),
            new Vector3(0f, 0f, 0f),
            new Vector3(3f, 0f, 0f));

        var rect = ScreenRect.FromCorners(Vector2.Zero, harness.ViewportSize);

        var actual = new List<SceneNode>();
        BoxSelectQuery.Query(harness.Scene, in rect, harness.ViewportSize, BoxSelectMode.Intersect, actual);

        actual.Count.ShouldBe(3);
    }

    [Fact]
    public void A_node_carrying_both_a_light_and_a_brush_is_reported_once()
    {
        var harness = new ViewportHarness();

        var node = new SceneNode("LampBlock")
        {
            Brush = Brush.CreateBox(Vector3.Zero, new Vector3(1f, 1f, 1f)),
            Light = new Light { Kind = LightKind.Point, Range = 4f },
        };

        harness.Scene.Root.AddChild(node);
        harness.Scene.Camera.Position = new Vector3(0f, 0f, 12f);
        harness.Scene.Camera.LookAt(Vector3.Zero);

        var rect = ScreenRect.FromCorners(Vector2.Zero, harness.ViewportSize);

        var actual = new List<SceneNode>();
        BoxSelectQuery.Query(harness.Scene, in rect, harness.ViewportSize, BoxSelectMode.Intersect, actual);

        // Already in the spatial index through its brush.
        actual.Count.ShouldBe(1);
    }

    // Independent of BoxSelectQuery: projects each lamp and tests the icon rect.
    private static SceneNode[] Oracle(ViewportHarness harness, in ScreenRect rect, BoxSelectMode mode)
    {
        Matrix4x4 viewProjection = harness.Scene.Camera.GetViewProjection();
        Vector2 viewport = harness.ViewportSize;
        var hits = new List<SceneNode>();

        foreach (SceneNode node in harness.Scene.Root.Children)
        {
            if (node.Light is null)
                continue;

            Vector4 clip = Vector4.Transform(new Vector4(node.WorldPosition, 1f), viewProjection);
            clip.W.ShouldBeGreaterThan(0.01f, $"'{node.Name}' straddles the eye plane");

            var pixel = new Vector2(
                ((clip.X / clip.W) + 1f) * 0.5f * viewport.X,
                (1f - (clip.Y / clip.W)) * 0.5f * viewport.Y);

            float R = LightPicking.PixelRadius(harness.Scene.Camera, viewport, node.WorldPosition);
            var icon = new ScreenRect(
                new Vector2(pixel.X - R, pixel.Y - R),
                new Vector2(pixel.X + R, pixel.Y + R));

            bool covered = mode == BoxSelectMode.Contain
                ? rect.Contains(in icon)
                : rect.Intersects(in icon);

            if (covered)
                hits.Add(node);
        }

        return [.. hits];
    }
}
