using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Which lights a frame gets, and in what order.</summary>
public sealed class SceneLightTests
{
    [Theory]
    [InlineData(0f, -1f, 0f)]
    [InlineData(-0.35f, -0.85f, -0.4f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0.3f, 0.9f, -0.2f)]
    public void A_directional_light_travels_the_way_it_was_pointed(float x, float y, float z)
    {
        // A mirrored direction throws nothing: the sun lights the underside
        // of everything and the scene just looks dim.
        var wanted = Vector3.Normalize(new Vector3(x, y, z));

        var scene = new Scene("directional");
        SceneNode node = scene.Root.CreateChild("Sun");
        node.LocalRotation = Light.RotationForDirection(wanted);
        node.Light = new Light { Kind = LightKind.Directional, Intensity = 1f };

        var view = new RenderView();
        scene.BuildRenderView(scene.Camera, view);

        view.LightCount.ShouldBe(1);
        RenderLight light = view.Lights[0];
        light.IsDirectional.ShouldBeTrue();

        var actual = new Vector3(light.PositionRange.X, light.PositionRange.Y, light.PositionRange.Z);
        Vector3.Dot(actual, wanted).ShouldBe(1f, 1e-4f,
            "the collected direction should be the one the caller asked for, not its mirror");
    }

    [Fact]
    public void A_light_pointed_straight_down_is_not_a_degenerate_rotation()
    {
        // Straight down is parallel to world up, so the cross product is zero
        // and a naive basis is NaN.
        var rotation = Light.RotationForDirection(-Vector3.UnitY);

        float.IsNaN(rotation.X + rotation.Y + rotation.Z + rotation.W).ShouldBeFalse();
        rotation.Length().ShouldBe(1f, 1e-4f);
    }

    [Fact]
    public void A_direction_with_no_length_is_refused()
    {
        Should.Throw<ArgumentException>(() => Light.RotationForDirection(Vector3.Zero));
    }

    private static SceneNode AddLight(
        Scene scene, string name, Vector3 position, LightKind kind = LightKind.Point, float range = 100f)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = position;
        node.Light = new Light { Kind = kind, Range = range, Color = Vector3.One };
        return node;
    }

    private static RenderView Build(Scene scene, Vector3 cameraPosition)
    {
        var view = new RenderView();
        scene.Camera.Position = cameraPosition;
        scene.BuildRenderView(scene.Camera, view);
        return view;
    }

    [Fact]
    public void A_light_is_collected_when_attached_and_dropped_when_detached()
    {
        var scene = new Scene("Lights");
        SceneNode node = AddLight(scene, "Lamp", new Vector3(1f, 0f, 0f));

        Build(scene, Vector3.Zero).LightCount.ShouldBe(1);

        node.Light = null;
        Build(scene, Vector3.Zero).LightCount.ShouldBe(0);
    }

    [Fact]
    public void Removing_the_node_removes_its_light()
    {
        var scene = new Scene("Lights");
        SceneNode node = AddLight(scene, "Lamp", new Vector3(1f, 0f, 0f));

        scene.Root.RemoveChild(node);

        Build(scene, Vector3.Zero).LightCount.ShouldBe(0);
    }

    [Fact]
    public void A_disabled_light_contributes_nothing()
    {
        var scene = new Scene("Lights");
        SceneNode node = AddLight(scene, "Lamp", new Vector3(1f, 0f, 0f));
        node.Light!.Enabled = false;

        RenderView view = Build(scene, Vector3.Zero);
        view.LightCount.ShouldBe(0);
        // Not dropped either: it was never a candidate.
        view.LightsDropped.ShouldBe(0);
    }

    [Fact]
    public void Lights_arrive_nearest_first()
    {
        var scene = new Scene("Lights");
        AddLight(scene, "Far", new Vector3(50f, 0f, 0f));
        AddLight(scene, "Near", new Vector3(2f, 0f, 0f));
        AddLight(scene, "Middle", new Vector3(10f, 0f, 0f));

        RenderView view = Build(scene, Vector3.Zero);

        view.LightCount.ShouldBe(3);
        view.Lights[0].PositionRange.X.ShouldBe(2f);
        view.Lights[1].PositionRange.X.ShouldBe(10f);
        view.Lights[2].PositionRange.X.ShouldBe(50f);
    }

    [Fact]
    public void A_directional_light_outranks_every_point_light()
    {
        // A sun has no position. Sorted by distance, a nearby lamp could push
        // it out of the list.
        var scene = new Scene("Lights");
        AddLight(scene, "VeryNear", new Vector3(0.1f, 0f, 0f));
        AddLight(scene, "Sun", Vector3.Zero, LightKind.Directional);

        RenderView view = Build(scene, Vector3.Zero);

        view.LightCount.ShouldBe(2);
        view.Lights[0].IsDirectional.ShouldBeTrue();
    }

    [Fact]
    public void Past_the_cap_the_furthest_lights_are_dropped_and_counted()
    {
        var scene = new Scene("Lights");
        int total = RenderView.MaxLights + 3;
        for (int i = 0; i < total; i++)
            AddLight(scene, $"Lamp{i}", new Vector3(total - i, 0f, 0f));

        RenderView view = Build(scene, Vector3.Zero);

        view.LightCount.ShouldBe(RenderView.MaxLights);
        view.LightsDropped.ShouldBe(3, "the overflow must be reported, not absorbed");

        for (int i = 1; i < view.LightCount; i++)
        {
            view.Lights[i].PositionRange.X
                .ShouldBeGreaterThanOrEqualTo(view.Lights[i - 1].PositionRange.X);
        }
        view.Lights[0].PositionRange.X.ShouldBe(1f);
    }

    [Fact]
    public void Two_builds_of_an_unchanged_scene_choose_the_same_lights()
    {
        var scene = new Scene("Lights");
        for (int i = 0; i < RenderView.MaxLights + 4; i++)
        {
            // Equidistant: every comparison is a tie, so only registration
            // order can decide.
            AddLight(scene, $"Lamp{i}", new Vector3(0f, 5f, 0f));
        }

        RenderView first = Build(scene, Vector3.Zero);
        RenderView second = Build(scene, Vector3.Zero);

        first.LightCount.ShouldBe(second.LightCount);
        for (int i = 0; i < first.LightCount; i++)
            first.Lights[i].ShouldBe(second.Lights[i]);
    }

    [Fact]
    public void Intensity_is_folded_into_the_uploaded_colour()
    {
        var scene = new Scene("Lights");
        SceneNode node = AddLight(scene, "Lamp", new Vector3(1f, 0f, 0f));
        node.Light!.Color = new Vector3(0.5f, 0.25f, 0f);
        node.Light.Intensity = 4f;

        RenderView view = Build(scene, Vector3.Zero);

        view.Lights[0].ColorIntensity.X.ShouldBe(2f);
        view.Lights[0].ColorIntensity.Y.ShouldBe(1f);
    }

    [Fact]
    public void A_point_light_carries_its_range_and_a_directional_one_does_not()
    {
        var scene = new Scene("Lights");
        AddLight(scene, "Point", new Vector3(1f, 0f, 0f), LightKind.Point, range: 7f);
        AddLight(scene, "Sun", Vector3.Zero, LightKind.Directional);

        RenderView view = Build(scene, Vector3.Zero);

        RenderLight sun = view.Lights[0];
        RenderLight point = view.Lights[1];

        sun.IsDirectional.ShouldBeTrue();
        sun.PositionRange.W.ShouldBe(0f);
        point.IsDirectional.ShouldBeFalse();
        point.PositionRange.W.ShouldBe(7f);
    }

    [Fact]
    public void A_negative_intensity_or_range_is_refused()
    {
        var light = new Light();
        Should.Throw<ArgumentOutOfRangeException>(() => light.Intensity = -1f);
        Should.Throw<ArgumentOutOfRangeException>(() => light.Range = 0f);
    }

    [Fact]
    public void A_light_does_not_make_a_node_pickable()
    {
        // Lights stay out of the BVH. PhysicsFlags.Default has CanCollide and
        // CanQuery, so an indexed lamp would be hit by rays and characters.
        var scene = new Scene("Lights");
        AddLight(scene, "Lamp", new Vector3(0f, 0f, -5f));

        bool hit = scene.Raycast(new Ray3(Vector3.Zero, -Vector3.UnitZ), out _);

        hit.ShouldBeFalse();
    }
}
