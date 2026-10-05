using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The spheres round a selected entity, one per distance its class declares.
/// </summary>
public sealed class EntityDistanceOverlayTests
{
    private const string Sound = "test_sound";
    private const string Reach = "test_reach";
    private const string Plain = "test_plain";
    private const string Bare = "test_bare";

    private const float MinDistance = 2f;
    private const float MaxDistance = 30f;

    // Classes that exist only as .sentdef bytes, the way the editor learns any
    // class. The sound is shaped as point_sound. Naming the real one would load
    // its assembly after the shared catalogue was read, and that throws.
    private static readonly EntitySchemaCatalog Schemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema(Sound, keyvalues:
        [
            Setting("sound", KeyvalueType.AssetSound, ""),
            Setting("volume", KeyvalueType.Float, "1"),
            Setting("mindistance", KeyvalueType.Distance, "2"),
            Setting("maxdistance", KeyvalueType.Distance, "30"),
        ]),
        new EntitySchema(Reach, keyvalues: [Setting("reach", KeyvalueType.Distance, "5")]),
        new EntitySchema(Plain, keyvalues: [Setting("speed", KeyvalueType.Float, "5")]),
        new EntitySchema(Bare),
    ]));

    private static KeyvalueDescriptor Setting(string name, KeyvalueType type, string value) =>
        new(name, "", "", value, type, KeyvalueWidget.Auto, float.NaN, float.NaN, 0u,
            KeyvalueDescriptor.NoChoices);

    private static Scene NewScene() => new("Distances") { EntitySchemas = Schemas };

    private static SceneNode Add(
        Scene scene, string className, Vector3 position, params (string Key, string Value)[] keyvalues)
    {
        var data = new EntityData(className);
        foreach ((string key, string value) in keyvalues)
            data.SetValue(key, value);

        var node = new SceneNode(className) { LocalPosition = position, Entity = data };
        scene.Root.AddChild(node);
        return node;
    }

    private static SceneNode AddSelected(
        Scene scene, string className, Vector3 position, params (string Key, string Value)[] keyvalues)
    {
        SceneNode node = Add(scene, className, position, keyvalues);
        scene.Selection.Add(node);
        return node;
    }

    [Fact]
    public void A_selected_sound_draws_both_its_distances()
    {
        Scene scene = NewScene();
        AddSelected(scene, Sound, Vector3.Zero);
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        overlay.DrawnLastDraw.ShouldBe(2);
        overlay.SkippedLastDraw.ShouldBe(0);
        output.VertexCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_sound_that_is_not_selected_draws_nothing()
    {
        Scene scene = NewScene();
        Add(scene, Sound, Vector3.Zero);
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        overlay.DrawnLastDraw.ShouldBe(0);
        output.VertexCount.ShouldBe(0);
    }

    [Fact]
    public void Each_distance_is_a_sphere_of_its_radius_round_the_node()
    {
        Scene scene = NewScene();
        SceneNode sound = AddSelected(
            scene, Sound, new Vector3(3f, 1f, -2f), ("mindistance", "1.5"), ("maxdistance", "12"));
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        ShouldBeSphere(Coloured(output, overlay.Color), sound.WorldPosition, 1.5f);
        ShouldBeSphere(Coloured(output, overlay.OuterColor), sound.WorldPosition, 12f);
    }

    [Fact]
    public void The_spheres_sit_at_the_nodes_world_position_under_a_moved_parent()
    {
        Scene scene = NewScene();
        SceneNode door = scene.Root.CreateChild("door");
        door.LocalPosition = new Vector3(10f, 0f, 4f);
        door.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var sound = new SceneNode("hum") { LocalPosition = new Vector3(2f, 1f, 0f), Entity = new EntityData(Sound) };
        door.AddChild(sound);
        scene.Selection.Add(sound);
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        sound.WorldPosition.ShouldNotBe(sound.LocalPosition);
        ShouldBeSphere(Coloured(output, overlay.Color), sound.WorldPosition, MinDistance);
        ShouldBeSphere(Coloured(output, overlay.OuterColor), sound.WorldPosition, MaxDistance);
    }

    [Fact]
    public void A_distance_the_level_does_not_write_is_drawn_at_the_class_default()
    {
        Scene scene = NewScene();
        SceneNode sound = AddSelected(scene, Sound, Vector3.Zero, ("maxdistance", "9"));
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        ShouldBeSphere(Coloured(output, overlay.Color), sound.WorldPosition, MinDistance);
        ShouldBeSphere(Coloured(output, overlay.OuterColor), sound.WorldPosition, 9f);
    }

    [Fact]
    public void The_smaller_distance_is_the_bright_one_whichever_setting_holds_it()
    {
        Scene scene = NewScene();
        SceneNode sound = AddSelected(scene, Sound, Vector3.Zero, ("mindistance", "20"), ("maxdistance", "4"));
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        ShouldBeSphere(Coloured(output, overlay.Color), sound.WorldPosition, 4f);
        ShouldBeSphere(Coloured(output, overlay.OuterColor), sound.WorldPosition, 20f);
    }

    [Fact]
    public void The_larger_distance_is_dimmer_than_the_entity_marker()
    {
        var overlay = new EntityDistanceOverlay();

        overlay.Color.ShouldBe(EntityMarkerOverlay.DefaultColor);
        overlay.OuterColor.Length().ShouldBeLessThan(overlay.Color.Length());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("far")]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e39")]
    public void A_value_that_is_no_length_draws_nothing(string written)
    {
        Scene scene = NewScene();
        AddSelected(scene, Reach, Vector3.Zero, ("reach", written));
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        overlay.DrawnLastDraw.ShouldBe(0);
        output.VertexCount.ShouldBe(0);
    }

    [Fact]
    public void A_bad_value_does_not_hide_the_entitys_other_distance()
    {
        Scene scene = NewScene();
        SceneNode sound = AddSelected(scene, Sound, Vector3.Zero, ("mindistance", "near"));
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        overlay.DrawnLastDraw.ShouldBe(1);
        ShouldBeSphere(Coloured(output, overlay.Color), sound.WorldPosition, MaxDistance);
    }

    [Fact]
    public void A_class_with_no_distance_setting_draws_nothing()
    {
        Scene scene = NewScene();
        AddSelected(scene, Plain, Vector3.Zero, ("speed", "8"));
        AddSelected(scene, Bare, Vector3.Zero);
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        overlay.DrawnLastDraw.ShouldBe(0);
        output.VertexCount.ShouldBe(0);
    }

    [Fact]
    public void A_class_the_schemas_do_not_know_draws_nothing()
    {
        Scene scene = NewScene();
        AddSelected(scene, "from_another_game", Vector3.Zero, ("maxdistance", "30"));
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        output.VertexCount.ShouldBe(0);
    }

    [Fact]
    public void Spheres_past_the_cap_are_counted_and_not_drawn()
    {
        Scene scene = NewScene();
        for (int i = 0; i < 5; i++)
            AddSelected(scene, Sound, new Vector3(i * 40f, 0f, 0f));
        var capped = new EntityDistanceOverlay { MaxSpheres = 3 };
        var cappedOutput = new DebugDraw();
        var one = new EntityDistanceOverlay { MaxSpheres = 1 };
        var oneOutput = new DebugDraw();

        capped.Draw(cappedOutput, scene);
        one.Draw(oneOutput, scene);

        capped.DrawnLastDraw.ShouldBe(3);
        capped.SkippedLastDraw.ShouldBe(7);
        cappedOutput.VertexCount.ShouldBe(3 * oneOutput.VertexCount);
    }

    [Fact]
    public void A_disabled_overlay_draws_nothing()
    {
        Scene scene = NewScene();
        AddSelected(scene, Sound, Vector3.Zero);
        var overlay = new EntityDistanceOverlay { Enabled = false };
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        output.VertexCount.ShouldBe(0);
        overlay.DrawnLastDraw.ShouldBe(0);
    }

    [Fact]
    public void A_second_draw_of_the_same_scene_allocates_nothing()
    {
        Scene scene = NewScene();
        for (int i = 0; i < 8; i++)
            AddSelected(scene, Sound, new Vector3(i * 3f, 0f, 0f), ("maxdistance", "12.5"));
        AddSelected(scene, Reach, Vector3.Zero, ("reach", "far"));
        AddSelected(scene, Plain, Vector3.Zero);
        var overlay = new EntityDistanceOverlay();
        var output = new DebugDraw();

        for (int i = 0; i < 200; i++)
            Frame(scene, overlay, output);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2_000; i++)
            Frame(scene, overlay, output);
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0);
        overlay.DrawnLastDraw.ShouldBe(16);

        static void Frame(Scene scene, EntityDistanceOverlay overlay, DebugDraw output)
        {
            output.Clear();
            overlay.Draw(output, scene);
        }
    }

    [Fact]
    public void The_editor_draws_the_distances_of_a_selected_sound()
    {
        Scene scene = NewScene();
        var renderer = new CompilingRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));
        var host = new SceneEditorHost(
            NullLoggerFactory.Instance, scene, renderer,
            new InputManager(NullLogger<InputManager>.Instance));
        scene.Camera.Position = new Vector3(0f, 0f, 12f);
        scene.Camera.LookAt(Vector3.Zero);
        SceneNode sound = Add(scene, Sound, new Vector3(1f, 0f, 0f));

        var unselected = new DebugDraw();
        host.Draw(unselected);
        Coloured(unselected, EntityDistanceOverlay.DefaultOuterColor).ShouldBeEmpty();

        scene.Selection.Add(sound);

        var selected = new DebugDraw();
        host.Draw(selected);
        ShouldBeSphere(
            Coloured(selected, EntityDistanceOverlay.DefaultOuterColor), sound.WorldPosition, MaxDistance);

        // The marker is the same green, and all of it is well inside the near sphere.
        List<Vector3> bright = Coloured(selected, EntityDistanceOverlay.DefaultColor);
        bright.RemoveAll(vertex => Vector3.Distance(vertex, sound.WorldPosition) < MinDistance * 0.75f);
        ShouldBeSphere(bright, sound.WorldPosition, MinDistance);
    }

    // A ring in each world plane, or it is a circle and not a sphere.
    private static void ShouldBeSphere(List<Vector3> vertices, Vector3 centre, float radius)
    {
        vertices.ShouldNotBeEmpty();

        float tolerance = radius * 1e-4f;
        Vector3 reach = Vector3.Zero;
        foreach (Vector3 vertex in vertices)
        {
            Vector3 offset = vertex - centre;
            offset.Length().ShouldBe(radius, tolerance);
            reach = Vector3.Max(reach, Vector3.Abs(offset));
        }

        reach.X.ShouldBe(radius, tolerance);
        reach.Y.ShouldBe(radius, tolerance);
        reach.Z.ShouldBe(radius, tolerance);
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
}
