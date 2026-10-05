using System;
using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>The outline that shows a part brush whose render bit is off.</summary>
public sealed class VolumeOverlayTests
{
    [Fact]
    public void A_hidden_part_is_outlined()
    {
        var scene = new Scene("Test");
        AddVolume(scene, "trigger");
        var overlay = new VolumeOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        overlay.DrawnLastDraw.ShouldBe(1);
        output.VertexCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_drawn_part_is_not_outlined()
    {
        var scene = new Scene("Test");
        AddPart(scene, "part");
        var overlay = new VolumeOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        overlay.DrawnLastDraw.ShouldBe(0);
        output.VertexCount.ShouldBe(0);
    }

    [Fact]
    public void A_world_brush_with_the_bit_off_is_not_outlined()
    {
        // The static world still draws it, so it is not a volume.
        var scene = new Scene("Test");
        SceneNode world = scene.Root.CreateChild("world");
        world.Brush = CreateUnitBrush();
        world.IsRendered = false;
        var overlay = new VolumeOverlay();

        overlay.Draw(new DebugDraw(), scene);

        overlay.DrawnLastDraw.ShouldBe(0);
    }

    [Fact]
    public void Hiding_and_showing_a_part_moves_it_in_and_out_of_the_overlay()
    {
        var scene = new Scene("Test");
        SceneNode node = AddPart(scene, "part");
        var overlay = new VolumeOverlay();

        node.IsRendered = false;
        overlay.Draw(new DebugDraw(), scene);
        overlay.DrawnLastDraw.ShouldBe(1);

        node.IsRendered = true;
        overlay.Draw(new DebugDraw(), scene);
        overlay.DrawnLastDraw.ShouldBe(0);
    }

    [Fact]
    public void Every_part_is_outlined_by_one_overlay_and_not_the_other()
    {
        var scene = new Scene("Test");
        AddPart(scene, "part");
        AddVolume(scene, "trigger");
        var parts = new PartBrushOverlay();
        var volumes = new VolumeOverlay();

        parts.Draw(new DebugDraw(), scene);
        volumes.Draw(new DebugDraw(), scene);

        parts.DrawnLastDraw.ShouldBe(1);
        volumes.DrawnLastDraw.ShouldBe(1);
    }

    [Fact]
    public void Volumes_do_not_spend_the_part_budget()
    {
        var scene = new Scene("Test");
        for (int i = 0; i < 10; i++)
            AddVolume(scene, $"trigger{i}");
        for (int i = 0; i < 4; i++)
            AddPart(scene, $"part{i}");
        var parts = new PartBrushOverlay { MaxOutlines = 4 };

        parts.Draw(new DebugDraw(), scene);

        parts.DrawnLastDraw.ShouldBe(4);
        parts.SkippedLastDraw.ShouldBe(0);
    }

    [Fact]
    public void The_outline_has_its_own_colour()
    {
        var scene = new Scene("Test");
        AddVolume(scene, "trigger");
        var output = new DebugDraw();

        new VolumeOverlay().Draw(output, scene);

        VolumeOverlay.DefaultColor.ShouldNotBe(PartBrushOverlay.DefaultColor);
        VolumeOverlay.DefaultColor.ShouldNotBe(SubtractiveBrushOverlay.DefaultColor);
        VolumeOverlay.DefaultColor.ShouldNotBe(SelectionOutline.SelectedColor);

        CountOf(output, VolumeOverlay.DefaultColor).ShouldBe(output.VertexCount);
    }

    [Fact]
    public void The_outline_follows_the_node_rather_than_the_brush()
    {
        var scene = new Scene("Test");
        SceneNode volume = AddVolume(scene, "trigger");
        volume.LocalPosition = new Vector3(50f, 0f, 0f);
        var output = new DebugDraw();

        new VolumeOverlay().Draw(output, scene);

        ReadOnlySpan<float> data = output.Vertices;
        bool anyNearTheVolume = false;
        for (int i = 0; i + 5 < data.Length; i += 6)
        {
            if (data[i] > 45f)
                anyNearTheVolume = true;
        }
        anyNearTheVolume.ShouldBeTrue();
    }

    [Fact]
    public void The_budget_is_disclosed_rather_than_silently_truncating()
    {
        var scene = new Scene("Test");
        for (int i = 0; i < 10; i++)
            AddVolume(scene, $"trigger{i}");
        var overlay = new VolumeOverlay { MaxOutlines = 4 };

        overlay.Draw(new DebugDraw(), scene);

        overlay.DrawnLastDraw.ShouldBe(4);
        overlay.SkippedLastDraw.ShouldBe(6);
    }

    [Fact]
    public void A_disabled_overlay_draws_nothing_and_reports_nothing_skipped()
    {
        var scene = new Scene("Test");
        AddVolume(scene, "trigger");
        var overlay = new VolumeOverlay { Enabled = false };
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        output.VertexCount.ShouldBe(0);
        overlay.DrawnLastDraw.ShouldBe(0);
        overlay.SkippedLastDraw.ShouldBe(0);
    }

    [Fact]
    public void The_editor_draws_a_hidden_part_in_the_volume_colour_only()
    {
        var scene = new Scene("Editor");
        SceneNode part = AddPart(scene, "door");
        var renderer = new CompilingRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));
        var host = new SceneEditorHost(
            NullLoggerFactory.Instance, scene, renderer,
            new InputManager(NullLogger<InputManager>.Instance));

        var shown = new DebugDraw();
        host.Draw(shown);
        int edges = CountOf(shown, PartBrushOverlay.DefaultColor);
        edges.ShouldBeGreaterThan(0);
        CountOf(shown, VolumeOverlay.DefaultColor).ShouldBe(0);

        part.IsRendered = false;

        var hidden = new DebugDraw();
        host.Draw(hidden);
        CountOf(hidden, VolumeOverlay.DefaultColor).ShouldBe(edges);
        CountOf(hidden, PartBrushOverlay.DefaultColor).ShouldBe(0);
    }

    // Six floats per vertex: position, then colour.
    private static int CountOf(DebugDraw output, Vector3 color)
    {
        ReadOnlySpan<float> data = output.Vertices;
        int count = 0;
        for (int i = 0; i + 5 < data.Length; i += 6)
        {
            if (new Vector3(data[i + 3], data[i + 4], data[i + 5]) == color)
                count++;
        }

        return count;
    }

    private static SceneNode AddPart(Scene scene, string name)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.BrushKind = BrushKind.Part;
        node.Brush = CreateUnitBrush();
        return node;
    }

    private static SceneNode AddVolume(Scene scene, string name)
    {
        SceneNode node = AddPart(scene, name);
        node.IsRendered = false;
        return node;
    }

    private static Brush CreateUnitBrush() =>
        Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
}
