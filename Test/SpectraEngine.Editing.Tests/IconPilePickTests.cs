using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editing.Selection;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// A click on an icon of a pile takes that icon's node, where the icon is
/// drawn. Only what is drawn on a point is found on it.
/// </summary>
public sealed class IconPilePickTests
{
    [Fact]
    public void A_ray_through_each_drawn_icon_of_a_pile_picks_that_entity()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        SceneIcon[] markers = rig.Place().Markers.ToArray();

        for (int i = 0; i < lift.Length; i++)
            rig.Pick(markers[i].At).ShouldBeSameAs(lift[i], $"icon {i}");
    }

    [Fact]
    public void A_ray_anywhere_on_a_drawn_icon_picks_it_and_one_beside_the_row_picks_nothing()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        SceneIcon[] markers = rig.Place().Markers.ToArray();
        Vector3 nearlyRim = rig.Camera.Up * (markers[0].Radius * 0.9f);

        for (int i = 0; i < lift.Length; i++)
        {
            rig.Pick(markers[i].At + nearlyRim).ShouldBeSameAs(lift[i], $"above icon {i}");
            rig.Pick(markers[i].At - nearlyRim).ShouldBeSameAs(lift[i], $"below icon {i}");
        }

        rig.Pick(markers[0].At - (rig.Camera.Right * (markers[0].Radius * 1.2f))).ShouldBeNull();
        rig.Pick(markers[3].At + (rig.Camera.Right * (markers[3].Radius * 1.2f))).ShouldBeNull();
    }

    [Fact]
    public void A_press_on_each_icon_of_a_pile_selects_its_entity()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(new Vector3(2f, 1f, 0f));
        SceneIcon[] markers = rig.Place().Markers.ToArray();

        for (int i = 0; i < lift.Length; i++)
            rig.Click(markers[i].At).ShouldBeSameAs(lift[i], $"icon {i}");
    }

    [Fact]
    public void The_cursor_over_an_icon_of_a_pile_is_over_its_entity()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        SceneIcon[] markers = rig.Place().Markers.ToArray();

        for (int i = 0; i < lift.Length; i++)
        {
            rig.Harness.Move(rig.Harness.WorldToScreen(markers[i].At));
            rig.Harness.Viewport.HoveredNode.ShouldBeSameAs(lift[i], $"icon {i}");
        }
    }

    [Fact]
    public void The_point_of_a_pile_of_four_lies_between_two_icons_and_picks_nothing()
    {
        var rig = new IconPileRig();
        rig.AddLift(Vector3.Zero);

        rig.Pick(Vector3.Zero).ShouldBeNull();

        // So a press there starts a marquee, as on empty space.
        rig.Harness.Viewport.ClassifyPress(rig.Harness.Frame(rig.Harness.CenterPixel))
            .ShouldBe(ViewportDragMode.BoxSelect);
    }

    [Fact]
    public void The_point_of_a_pile_of_three_picks_the_icon_drawn_on_it()
    {
        var rig = new IconPileRig();
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneNode middle = rig.AddEntity(IconPileRig.Relay, Vector3.Zero);
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);

        rig.Place().Markers[1].IsMoved.ShouldBeFalse();
        rig.Pick(Vector3.Zero).ShouldBeSameAs(middle);
        rig.Click(Vector3.Zero).ShouldBeSameAs(middle);
    }

    [Fact]
    public void An_icon_that_was_not_moved_is_picked_on_its_node_as_before()
    {
        var rig = new IconPileRig();
        rig.AddLift(new Vector3(-3f, 0f, 0f));
        SceneNode alone = rig.AddEntity(IconPileRig.Sound, new Vector3(3f, 1f, 0f));

        rig.Place().Markers[4].IsMoved.ShouldBeFalse();
        rig.Pick(alone.WorldPosition).ShouldBeSameAs(alone);
        rig.Click(alone.WorldPosition).ShouldBeSameAs(alone);
    }

    [Fact]
    public void A_light_and_a_sound_at_one_point_stand_side_by_side_and_each_is_picked_at_its_icon()
    {
        var rig = new IconPileRig();
        SceneNode sound = rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneNode lamp = rig.AddLamp(Vector3.Zero);

        SceneIcons icons = rig.Place();
        SceneIcon lampIcon = icons.Lights[0];
        SceneIcon soundIcon = icons.Markers[0];

        // The sound comes first in the scene tree, so it is on the left.
        float half = lampIcon.Radius * ViewportGlyphs.Reach;
        Vector3.Dot(soundIcon.At, rig.Camera.Right).ShouldBe(-half, tolerance: 1e-5f);
        Vector3.Dot(lampIcon.At, rig.Camera.Right).ShouldBe(half, tolerance: 1e-5f);

        rig.Pick(lampIcon.At).ShouldBeSameAs(lamp);
        rig.Pick(soundIcon.At).ShouldBeSameAs(sound);
        rig.Click(lampIcon.At).ShouldBeSameAs(lamp);
        rig.Click(soundIcon.At).ShouldBeSameAs(sound);
    }

    [Fact]
    public void A_light_and_a_sound_at_one_point_are_each_drawn_where_they_are_picked()
    {
        var rig = new IconPileRig();
        rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        rig.AddLamp(Vector3.Zero);
        SceneIcons icons = rig.Place();
        var lamps = new DebugDraw();
        var sounds = new DebugDraw();

        // Each overlay with a set of its own, as apart as two can be.
        new LightOverlay().Draw(lamps, rig.Scene, rig.Camera, rig.ViewportSize);
        new EntityMarkerOverlay().Draw(sounds, rig.Scene, rig.Camera, rig.ViewportSize);

        AllWithin(DebugLines.Colored(lamps, IconPileRig.LampTint), icons.Lights[0]);
        AllWithin(DebugLines.Colored(sounds, IconPileRig.SoundTint), icons.Markers[0]);
        DebugLines.CountOf(lamps, IconPileRig.LampTint * LightOverlay.HomeLineDim).ShouldBe(5);
        DebugLines.CountOf(sounds, IconPileRig.SoundTint * LightOverlay.HomeLineDim).ShouldBe(5);

        static void AllWithin(List<Vector3> vertices, SceneIcon icon)
        {
            vertices.ShouldNotBeEmpty();
            foreach (Vector3 vertex in vertices)
                Vector3.Distance(vertex, icon.At).ShouldBeLessThan(icon.Radius * 1.6f);
        }
    }

    [Fact]
    public void In_a_flat_view_one_thing_above_another_stands_beside_it_and_each_is_picked_there()
    {
        // Seen from above, a lamp over a sound is on the same pixel.
        var rig = new IconPileRig();
        SceneNode sound = rig.AddEntity(IconPileRig.Sound, Vector3.Zero);
        SceneNode lamp = rig.AddLamp(new Vector3(0f, 3f, 0f));
        rig.Harness.Orbit(Vector3.Zero, 10f, 0.4f, -0.5f);
        rig.Harness.EditorCamera.SetView(EditorViewPreset.Top);
        rig.Camera.ProjectionKind.ShouldBe(CameraProjectionKind.Orthographic);

        SceneIcons icons = rig.Place();
        SceneIcon lampIcon = icons.Lights[0];
        SceneIcon soundIcon = icons.Markers[0];

        // Each stays at its own height, one icon width from the other across the screen.
        float width = lampIcon.Radius * ViewportGlyphs.Reach * 2f;
        Vector3.Dot(lampIcon.At - soundIcon.At, rig.Camera.Right).ShouldBe(width, tolerance: 1e-4f);
        Vector3.Dot(lampIcon.At - soundIcon.At, rig.Camera.Up).ShouldBe(0f, tolerance: 1e-4f);
        lampIcon.At.Y.ShouldBe(3f, tolerance: 1e-4f);
        soundIcon.At.Y.ShouldBe(0f, tolerance: 1e-4f);

        rig.Pick(lampIcon.At).ShouldBeSameAs(lamp);
        rig.Pick(soundIcon.At).ShouldBeSameAs(sound);
        rig.Pick(Vector3.Zero).ShouldBeNull();

        // The rim is where it is drawn, though a flat view's rays start a far plane back.
        Vector3 rim = rig.Camera.Right * lampIcon.Radius;
        rig.Pick(lampIcon.At + (rim * 0.95f)).ShouldBeSameAs(lamp);
        rig.Pick(lampIcon.At - (rim * 0.95f)).ShouldBeSameAs(lamp);
        rig.Pick(lampIcon.At + (rim * 1.05f)).ShouldBeNull();
        rig.Pick(lampIcon.At - (rim * 1.05f)).ShouldBeNull();
    }

    [Fact]
    public void A_selected_lights_shape_stays_on_the_light_when_its_icon_is_moved()
    {
        var rig = new IconPileRig();
        SceneNode lamp = rig.AddLamp(Vector3.Zero);
        rig.AddLamp(Vector3.Zero);
        rig.Scene.Selection.Add(lamp);
        var output = new DebugDraw();

        new LightOverlay().Draw(output, rig.Scene, rig.Camera, rig.ViewportSize);

        // A point light's shape is a sphere of its range round the light.
        rig.Place().Lights[0].IsMoved.ShouldBeTrue();
        DebugLines.Colored(output, IconPileRig.LampTint)
            .ShouldContain(vertex => MathF.Abs(vertex.Length() - 3f) < 1e-3f);
    }

    [Fact]
    public void A_face_in_front_of_a_moved_light_icon_is_still_picked_before_the_icon()
    {
        var rig = new IconPileRig();
        rig.AddLamp(new Vector3(0f, 0f, -6f));
        rig.AddLamp(new Vector3(0f, 0f, -6f));
        SceneNode brush = rig.Harness.AddBrush(Vector3.Zero, 1f);
        SceneIcon icon = rig.Place().Lights[1];

        // The ray must meet both, or the pick has nothing to choose between.
        icon.IsMoved.ShouldBeTrue();
        Ray3 ray = rig.RayThrough(icon.At);
        rig.Scene.Raycast(in ray, out _).ShouldBeTrue();
        rig.Pick(icon.At).ShouldNotBeNull();

        rig.Click(icon.At).ShouldBeSameAs(brush);
        rig.Scene.Selection.FaceNode.ShouldBeSameAs(brush);
        rig.Scene.Selection.FacePlane.ShouldBe(4);
    }

    [Fact]
    public void A_marquee_takes_a_pile_by_where_its_entities_are_not_where_they_are_drawn()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        SceneIcon[] markers = rig.Place().Markers.ToArray();
        float radius = markers[0].Radius / markers[0].Pixel;
        var taken = new List<SceneNode>();

        // Round the point alone, well inside the row.
        Vector2 point = rig.Harness.WorldToScreen(Vector3.Zero);
        var onPoint = ScreenRect.FromCorners(point - new Vector2(2f), point + new Vector2(2f));
        BoxSelectQuery.Query(rig.Scene, in onPoint, rig.ViewportSize, BoxSelectMode.Intersect, taken);
        taken.Count.ShouldBe(lift.Length);

        // Round the icon at the left-hand end alone, which is drawn clear of the point.
        Vector2 end = rig.Harness.WorldToScreen(markers[0].At);
        var onEnd = ScreenRect.FromCorners(end - new Vector2(radius), end + new Vector2(radius));
        BoxSelectQuery.Query(rig.Scene, in onEnd, rig.ViewportSize, BoxSelectMode.Intersect, taken);
        taken.ShouldBeEmpty();
    }

    [Fact]
    public void A_budget_set_on_the_overlay_is_the_budget_a_click_sees()
    {
        // Two may be glyphs. The near two are, and stand as a pair. The far
        // two stay on their point as diamonds.
        var rig = new IconPileRig();
        Vector3 back = new(4f, 0f, -4f);
        SceneNode[] near = [rig.AddEntity(IconPileRig.Sound, Vector3.Zero), rig.AddEntity(IconPileRig.Sound, Vector3.Zero)];
        SceneNode[] far = [rig.AddEntity(IconPileRig.Relay, back), rig.AddEntity(IconPileRig.Relay, back)];
        var icons = new SceneIcons();
        var overlay = new EntityMarkerOverlay(icons) { MaxIcons = 2 };
        rig.Harness.Viewport.Icons = icons;

        overlay.Draw(new DebugDraw(), rig.Scene, rig.Camera, rig.ViewportSize);
        SceneIcon[] markers = icons.Markers.ToArray();

        overlay.IconsLastDraw.ShouldBe(2);
        markers[0].IsMoved.ShouldBeTrue();
        markers[1].IsMoved.ShouldBeTrue();
        markers[2].IsMoved.ShouldBeFalse();
        markers[3].IsMoved.ShouldBeFalse();

        rig.Click(markers[0].At).ShouldBeSameAs(near[0]);
        rig.Click(markers[1].At).ShouldBeSameAs(near[1]);
        rig.Click(back).ShouldBeSameAs(far[0]);

        // With glyphs to spare the far two stand apart, and their point is empty.
        rig.Place().Markers[2].IsMoved.ShouldBeTrue();
        rig.Pick(back).ShouldBeNull();
    }

    [Fact]
    public void Two_sets_of_icons_with_the_same_budgets_agree_to_the_bit()
    {
        var random = new Random(5);
        var rig = new IconPileRig();
        for (int i = 0; i < 60; i++)
        {
            var at = new Vector3(random.Next(-4, 5), random.Next(-3, 4), random.Next(-6, 3)) * 0.35f;
            if (i % 3 == 0)
                rig.AddLamp(at);
            else
                rig.AddEntity(i % 2 == 0 ? IconPileRig.Sound : IconPileRig.Relay, at);
        }

        rig.Harness.Orbit(Vector3.Zero, 14f, 0.7f, -0.4f);
        var first = new SceneIcons();
        var second = new SceneIcons();

        // The first has placed another view before, so it has something to forget.
        first.Place(rig.Scene, rig.Camera, rig.ViewportSize);
        rig.Harness.Orbit(Vector3.Zero, 11f, 2.1f, -0.9f);
        first.Place(rig.Scene, rig.Camera, rig.ViewportSize);
        second.Place(rig.Scene, rig.Camera, rig.ViewportSize);

        first.Lights.ToArray().ShouldBe(second.Lights.ToArray());
        first.Markers.ToArray().ShouldBe(second.Markers.ToArray());

        int moved = 0;
        foreach (SceneIcon icon in first.Markers)
            moved += icon.IsMoved ? 1 : 0;

        moved.ShouldBeGreaterThan(10);
    }
}
