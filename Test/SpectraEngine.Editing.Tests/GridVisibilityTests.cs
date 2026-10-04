using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Viewport;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>Ground grid emission, grid mode and the fade envelope.</summary>
public sealed class GridVisibilityTests
{
    private static SceneEditorHost NewHost(Scene scene)
    {
        var renderer = new CompilingRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));

        return new SceneEditorHost(
            NullLoggerFactory.Instance,
            scene,
            renderer,
            new InputManager(NullLogger<InputManager>.Instance));
    }

    [Fact]
    public void The_grid_emits_whole_lines_and_writes_its_fade_as_metadata()
    {
        var grid = new GroundGrid();
        var output = new DebugDraw();
        var camera = new Camera { Position = new Vector3(0f, 5f, 0f) };

        grid.Draw(output, camera, increment: 1f, viewportHeight: 720f);

        output.VertexCount.ShouldBe(grid.DrawnLastDraw * 2,
            "each grid line must be ONE Line call; a vertex count above two per line means " +
            "the CPU-side segment fade is back, and with it the chunk pop it caused");
        grid.DrawnLastDraw.ShouldBeGreaterThan(4);
        grid.SkippedLastDraw.ShouldBe(0);

        // 48 is the continuous radius at this height, not the step-quantised reach.
        output.FadeCenter.ShouldBe(new Vector3(0f, 0f, 0f));
        output.FadeStart.ShouldBe(48f * 0.05f, tolerance: 0.001f);
        output.FadeEnd.ShouldBe(48f * 0.62f, tolerance: 0.001f);
        output.Opacity.ShouldBe(1f);
    }

    [Fact]
    public void A_faded_out_grid_emits_nothing_at_all()
    {
        var grid = new GroundGrid { Opacity = 0f };
        var output = new DebugDraw();
        var camera = new Camera { Position = new Vector3(0f, 5f, 0f) };

        grid.Draw(output, camera, increment: 1f, viewportHeight: 720f);

        output.VertexCount.ShouldBe(0,
            "a grid at zero opacity must cost zero: every line it emitted would be " +
            "discarded per pixel anyway");
        grid.DrawnLastDraw.ShouldBe(0);
    }

    [Fact]
    public void The_grid_opacity_multiplier_reaches_the_shader_through_the_buffer()
    {
        var grid = new GroundGrid { Opacity = 0.4f };
        var output = new DebugDraw();
        var camera = new Camera { Position = new Vector3(0f, 5f, 0f) };

        grid.Draw(output, camera, increment: 1f, viewportHeight: 720f);

        output.Opacity.ShouldBe(0.4f,
            "the envelope's alpha is metadata for the shader, never a colour multiply: " +
            "dimming a dark line's COLOUR over a lit floor does not fade it");
    }

    // At fov π/3 over 720 px a 1-unit cell is 12 px at height ~52 (coarsen)
    // and 16 px at height ~39 (refine). The band is between them.

    [Fact]
    public void The_spacing_coarsens_when_cells_get_too_small_and_does_not_flap_back()
    {
        var grid = new GroundGrid();
        var output = new DebugDraw();

        var high = new Camera { Position = new Vector3(0f, 60f, 0f) };
        grid.Draw(output, high, increment: 1f, viewportHeight: 720f);
        grid.CellSizeLastDraw.ShouldBe(2f);

        // Inside the band (finer level ≈14 px): stays coarse.
        var band = new Camera { Position = new Vector3(0f, 45f, 0f) };
        output.Clear();
        grid.Draw(output, band, increment: 1f, viewportHeight: 720f);
        grid.CellSizeLastDraw.ShouldBe(2f,
            "refining the moment the finer level clears the coarsen threshold is a flip-flop, " +
            "not hysteresis");

        var low = new Camera { Position = new Vector3(0f, 30f, 0f) };
        output.Clear();
        grid.Draw(output, low, increment: 1f, viewportHeight: 720f);
        grid.CellSizeLastDraw.ShouldBe(1f);
    }

    [Fact]
    public void A_fresh_grid_in_the_hysteresis_band_starts_fine_rather_than_coarse
        ()
    {
        var grid = new GroundGrid();
        var output = new DebugDraw();
        var band = new Camera { Position = new Vector3(0f, 45f, 0f) };

        grid.Draw(output, band, increment: 1f, viewportHeight: 720f);
        grid.CellSizeLastDraw.ShouldBe(1f);
    }

    [Fact]
    public void The_grid_mode_defaults_to_auto_and_the_set_verbs_move_it()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.GridModeName.ShouldBe("auto");

        host.Apply(EditorHostCommand.GridOn);
        host.GridModeName.ShouldBe("on");

        host.Apply(EditorHostCommand.GridOff);
        host.GridModeName.ShouldBe("off");

        host.Apply(EditorHostCommand.GridAuto);
        host.GridModeName.ShouldBe("auto");
    }

    [Fact]
    public void The_grid_verbs_stay_live_while_the_editor_is_suspended()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.Suspend();
        host.Apply(EditorHostCommand.GridOn);
        host.GridModeName.ShouldBe("on");
    }

    [Fact]
    public void The_envelope_ramps_the_grid_in_and_out_rather_than_cutting()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.Update(0.1);
        host.Grid.Opacity.ShouldBe(0f);

        host.Apply(EditorHostCommand.GridOn);
        host.Update(0.05);
        host.Grid.Opacity.ShouldBeGreaterThan(0.1f);
        host.Grid.Opacity.ShouldBeLessThan(1f);

        host.Update(0.05);
        host.Update(0.05);
        host.Grid.Opacity.ShouldBe(1f);

        // Fades out slower than in.
        host.Apply(EditorHostCommand.GridOff);
        host.Update(0.05);
        host.Grid.Opacity.ShouldBeGreaterThan(0f);
        host.Grid.Opacity.ShouldBeLessThan(0.9f);

        for (int i = 0; i < 6; i++)
            host.Update(0.05);
        host.Grid.Opacity.ShouldBe(0f);
    }
}

/// <summary>Which plane the grid is drawn on, and what decides its spacing there.</summary>
public sealed class GridPlaneTests
{
    private static Camera Ortho(EditorViewPreset preset, float height)
    {
        var camera = new Camera
        {
            AspectRatio = 16f / 9f,
            ProjectionKind = CameraProjectionKind.Orthographic,
            OrthographicHeight = height,
            Position = new Vector3(3f, 7f, -4f),
        };

        if (preset is EditorViewPreset.Top or EditorViewPreset.Bottom)
            camera.SetVerticalView(preset == EditorViewPreset.Top, EditorViewPresets.YawOf(preset));
        else
        {
            camera.Yaw = EditorViewPresets.YawOf(preset);
            camera.Pitch = EditorViewPresets.PitchOf(preset);
        }

        return camera;
    }

    private static DebugDraw DrawOn(GridPlane plane, Camera camera, float increment = 1f)
    {
        var grid = new GroundGrid();
        var output = new DebugDraw();

        grid.Draw(output, camera, increment, viewportHeight: 720f, plane);
        return output;
    }

    [Fact]
    public void A_front_view_draws_the_grid_on_the_xy_plane()
    {
        DebugDraw output = DrawOn(GridPlane.Front, Ortho(EditorViewPreset.Front, 20f));

        output.VertexCount.ShouldBeGreaterThan(0);

        // Plane through the origin, not one at the camera's depth.
        foreach (Vector3 vertex in Vertices(output))
            vertex.Z.ShouldBe(0f, 1e-4f);
    }

    [Fact]
    public void A_side_view_draws_the_grid_on_the_yz_plane()
    {
        DebugDraw output = DrawOn(GridPlane.Side, Ortho(EditorViewPreset.Right, 20f));

        foreach (Vector3 vertex in Vertices(output))
            vertex.X.ShouldBe(0f, 1e-4f);
    }

    [Fact]
    public void A_top_view_draws_the_grid_on_the_floor()
    {
        DebugDraw output = DrawOn(GridPlane.Ground, Ortho(EditorViewPreset.Top, 20f));

        foreach (Vector3 vertex in Vertices(output))
            vertex.Y.ShouldBe(0f, 1e-4f);
    }

    [Fact]
    public void A_top_view_coarsens_by_the_orthographic_height_and_not_the_eye()
    {
        var grid = new GroundGrid();

        // Under orthographic the eye sits at the focus, so its distance to the
        // plane is zero and cannot drive the spacing.
        Camera near = Ortho(EditorViewPreset.Top, 20f);
        grid.Draw(new DebugDraw(), near, 1f, 720f, GridPlane.Ground);
        float fine = grid.CellSizeLastDraw;

        Camera far = Ortho(EditorViewPreset.Top, 4000f);
        grid.Draw(new DebugDraw(), far, 1f, 720f, GridPlane.Ground);
        float coarse = grid.CellSizeLastDraw;

        fine.ShouldBeGreaterThan(0f);
        coarse.ShouldBeGreaterThan(fine);
    }

    [Fact]
    public void The_old_call_still_draws_on_the_floor()
    {
        var grid = new GroundGrid();
        var output = new DebugDraw();
        var camera = new Camera { Position = new Vector3(0f, 12f, 0f), AspectRatio = 16f / 9f };

        grid.Draw(output, camera, 1f, 720f);

        foreach (Vector3 vertex in Vertices(output))
            vertex.Y.ShouldBe(0f, 1e-4f);
    }

    private static List<Vector3> Vertices(DebugDraw output)
    {
        // Copied out: an iterator cannot hold a span across a yield.
        var vertices = new List<Vector3>(output.VertexCount);
        ReadOnlySpan<float> data = output.Vertices;
        int stride = data.Length / System.Math.Max(output.VertexCount, 1);

        for (int i = 0; i + 2 < data.Length; i += stride)
            vertices.Add(new Vector3(data[i], data[i + 1], data[i + 2]));

        return vertices;
    }
}
