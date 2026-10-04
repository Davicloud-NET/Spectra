using System;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>The outline that tells a part brush from a world brush.</summary>
public sealed class PartBrushOverlayTests
{
    [Fact]
    public void A_part_brush_is_outlined()
    {
        var scene = new Scene("Test");
        AddPart(scene, "part");
        var overlay = new PartBrushOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        overlay.DrawnLastDraw.ShouldBe(1);
        output.VertexCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_world_brush_is_not_outlined()
    {
        var scene = new Scene("Test");
        SceneNode world = scene.Root.CreateChild("world");
        world.Brush = CreateUnitBrush();
        var overlay = new PartBrushOverlay();
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        overlay.DrawnLastDraw.ShouldBe(0);
        output.VertexCount.ShouldBe(0);
    }

    [Fact]
    public void Converting_a_brush_moves_it_in_and_out_of_the_overlay()
    {
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("brush");
        node.Brush = CreateUnitBrush();
        var overlay = new PartBrushOverlay();

        node.BrushKind = BrushKind.Part;
        overlay.Draw(new DebugDraw(), scene);
        overlay.DrawnLastDraw.ShouldBe(1);

        node.BrushKind = BrushKind.World;
        overlay.Draw(new DebugDraw(), scene);
        overlay.DrawnLastDraw.ShouldBe(0);
    }

    [Fact]
    public void The_outline_follows_the_node_rather_than_the_brush()
    {
        var scene = new Scene("Test");
        SceneNode part = AddPart(scene, "part");
        part.LocalPosition = new Vector3(50f, 0f, 0f);
        var output = new DebugDraw();

        new PartBrushOverlay().Draw(output, scene);

        // Six floats per vertex: position, then colour.
        ReadOnlySpan<float> data = output.Vertices;
        bool anyNearThePart = false;
        for (int i = 0; i + 5 < data.Length; i += 6)
        {
            if (data[i] > 45f)
                anyNearThePart = true;
        }
        anyNearThePart.ShouldBeTrue();
    }

    [Fact]
    public void The_budget_is_disclosed_rather_than_silently_truncating()
    {
        var scene = new Scene("Test");
        for (int i = 0; i < 10; i++)
            AddPart(scene, $"part{i}");
        var overlay = new PartBrushOverlay { MaxOutlines = 4 };

        overlay.Draw(new DebugDraw(), scene);

        overlay.DrawnLastDraw.ShouldBe(4);
        overlay.SkippedLastDraw.ShouldBe(6);
    }

    [Fact]
    public void A_disabled_overlay_draws_nothing_and_reports_nothing_skipped()
    {
        var scene = new Scene("Test");
        AddPart(scene, "part");
        var overlay = new PartBrushOverlay { Enabled = false };
        var output = new DebugDraw();

        overlay.Draw(output, scene);

        output.VertexCount.ShouldBe(0);
        overlay.DrawnLastDraw.ShouldBe(0);
        overlay.SkippedLastDraw.ShouldBe(0);
    }

    private static SceneNode AddPart(Scene scene, string name)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.BrushKind = BrushKind.Part;
        node.Brush = CreateUnitBrush();
        return node;
    }

    private static Brush CreateUnitBrush() =>
        Brush.CreateBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
}
