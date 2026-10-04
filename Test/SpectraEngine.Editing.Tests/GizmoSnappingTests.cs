using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Grid snapping: <see cref="GridSnapSettings"/> arithmetic and a live drag.
/// The displacement snaps by default; <see cref="TranslateSnapMode.AbsoluteGrid"/>
/// snaps the destination.
/// </summary>
public sealed class GizmoSnappingTests
{
    private const float AlongAxis = 0.8f;
    private const float RoundTrip = 1e-3f;

    [Fact]
    public void The_default_increment_is_one_world_unit()
    {
        var snap = new GridSnapSettings();

        snap.Enabled.ShouldBeTrue();
        snap.Increment.ShouldBe(1f);
        GridSnapSettings.Presets.ShouldBe(new[] { 0.25f, 0.5f, 1f, 2f, 4f });
    }

    [Theory]
    [InlineData(1f, 4.2f, 4f)]
    [InlineData(1f, 4.7f, 5f)]
    [InlineData(1f, -4.2f, -4f)]
    [InlineData(0.25f, 4.2f, 4.25f)]
    [InlineData(0.5f, 4.2f, 4f)]
    [InlineData(2f, 4.2f, 4f)]
    [InlineData(4f, 6.5f, 8f)]
    public void A_coordinate_rounds_to_the_nearest_multiple(float increment, float value, float expected)
    {
        new GridSnapSettings { Increment = increment }.SnapScalar(value).ShouldBe(expected, 1e-4f);
    }

    [Fact]
    public void An_increment_must_be_positive()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new GridSnapSettings { Increment = 0f });
        Should.Throw<ArgumentOutOfRangeException>(() => new GridSnapSettings { Increment = -1f });
    }

    [Fact]
    public void Only_the_masked_components_are_rounded()
    {
        var snap = new GridSnapSettings();
        var value = new Vector3(4.2f, 0.3f, -7.9f);

        // An x-axis drag frees x only.
        snap.SnapMasked(value, GizmoHandles.FreeAxisMask(GizmoHandle.AxisX))
            .ShouldBe(new Vector3(4f, 0.3f, -7.9f));

        snap.SnapMasked(value, GizmoHandles.FreeAxisMask(GizmoHandle.PlaneZX))
            .ShouldBe(new Vector3(4f, 0.3f, -8f));

        snap.SnapMasked(value, GizmoHandles.FreeAxisMask(GizmoHandle.Screen))
            .ShouldBe(new Vector3(4f, 0f, -8f));
    }

    [Fact]
    public void The_toggle_modifier_inverts_snapping_in_both_directions()
    {
        var snap = new GridSnapSettings { Enabled = true, ToggleModifier = KeyModifiers.Alt };

        snap.IsActiveWith(KeyModifiers.None).ShouldBeTrue();
        snap.IsActiveWith(KeyModifiers.Alt).ShouldBeFalse();
        snap.IsActiveWith(KeyModifiers.Shift).ShouldBeTrue();
        snap.IsActiveWith(KeyModifiers.Alt | KeyModifiers.Shift).ShouldBeFalse();

        snap.Enabled = false;
        snap.IsActiveWith(KeyModifiers.None).ShouldBeFalse();
        snap.IsActiveWith(KeyModifiers.Alt).ShouldBeTrue();

        // No modifier assigned: no override.
        snap.ToggleModifier = KeyModifiers.None;
        snap.IsActiveWith(KeyModifiers.Alt).ShouldBeFalse();
    }

    [Fact]
    public void Cycling_walks_the_presets_and_clamps_at_the_ends()
    {
        var snap = new GridSnapSettings(); // 1.0

        snap.CyclePreset(1).ShouldBe(2f);
        snap.CyclePreset(1).ShouldBe(4f);
        snap.CyclePreset(1).ShouldBe(4f); // clamped, not wrapped
        snap.CyclePreset(-1).ShouldBe(2f);
        snap.CyclePreset(-3).ShouldBe(0.25f);
        snap.CyclePreset(-1).ShouldBe(0.25f);

        // An off-ladder increment enters at its nearest rung.
        snap.Increment = 3f;
        snap.CyclePreset(0).ShouldBe(4f);
    }

    [Fact]
    public void A_preset_can_be_selected_directly()
    {
        var snap = new GridSnapSettings();
        snap.SelectPreset(0);
        snap.Increment.ShouldBe(0.25f);

        Should.Throw<ArgumentOutOfRangeException>(() => snap.SelectPreset(GridSnapSettings.Presets.Count));
        Should.Throw<ArgumentOutOfRangeException>(() => snap.SelectPreset(-1));
    }

    [Fact]
    public void A_snapped_drag_lands_on_absolute_grid_multiples()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode node = harness.AddSelectedNode(Vector3.Zero);
        float length = harness.GeometryAt(Vector3.Zero).AxisLength;

        harness.Grab(Vector3.UnitX * (length * AlongAxis));
        harness.DragBy(Vector3.UnitX * 4.2f);
        harness.Release();

        node.LocalPosition.X.ShouldBe(4f, RoundTrip);
    }

    [Fact]
    public void A_snapped_drag_preserves_the_sub_grid_offset_by_default()
    {
        // 0.3 dragged by 3.9: the movement rounds to 4.0, so the node lands on
        // 4.3. Snapping the result would give 4.0.
        var harness = GizmoHarness.ThreeQuarterView();
        var start = new Vector3(0.3f, 0f, 0f);
        SceneNode node = harness.AddSelectedNode(start);
        float length = harness.GeometryAt(start).AxisLength;

        harness.Grab(start + Vector3.UnitX * (length * AlongAxis));
        harness.DragBy(Vector3.UnitX * 3.9f);
        harness.Release();

        node.LocalPosition.X.ShouldBe(4.3f, RoundTrip);
    }

    [Fact]
    public void Absolute_grid_mode_pulls_an_off_grid_selection_onto_the_grid()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        harness.Translate.Snap.Mode = TranslateSnapMode.AbsoluteGrid;
        var start = new Vector3(0.3f, 0f, 0f);
        SceneNode node = harness.AddSelectedNode(start);
        float length = harness.GeometryAt(start).AxisLength;

        harness.Grab(start + Vector3.UnitX * (length * AlongAxis));
        harness.DragBy(Vector3.UnitX * 3.9f);
        harness.Release();

        node.LocalPosition.X.ShouldBe(4f, RoundTrip);
    }

    [Fact]
    public void Delta_snapping_preserves_every_offset_of_a_multi_selection()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode first = harness.AddSelectedNode(Vector3.Zero, "First");
        SceneNode second = harness.AddSelectedNode(new Vector3(1.5f, 0f, 0f), "Second");
        Vector3 pivot = new(0.75f, 0f, 0f);
        float length = harness.GeometryAt(pivot).AxisLength;

        harness.Grab(pivot + Vector3.UnitX * (length * AlongAxis));
        harness.DragBy(Vector3.UnitX * 1.2f);
        harness.Release();

        first.LocalPosition.X.ShouldBe(1f, RoundTrip);
        second.LocalPosition.X.ShouldBe(2.5f, RoundTrip);
    }

    [Fact]
    public void Absolute_grid_mode_anchors_on_the_reference_node_of_a_multi_selection()
    {
        // Anchored on the reference node (last selected), not the pivot
        // average, or no node lands on the grid. Reference: 1.5 + ~1.2 -> 3.0.
        var harness = GizmoHarness.ThreeQuarterView();
        harness.Translate.Snap.Mode = TranslateSnapMode.AbsoluteGrid;
        SceneNode first = harness.AddSelectedNode(Vector3.Zero, "First");
        SceneNode reference = harness.AddSelectedNode(new Vector3(1.5f, 0f, 0f), "Reference");
        Vector3 pivot = new(0.75f, 0f, 0f);
        float length = harness.GeometryAt(pivot).AxisLength;

        harness.Grab(pivot + Vector3.UnitX * (length * AlongAxis));
        harness.DragBy(Vector3.UnitX * 1.2f);
        harness.Release();

        reference.LocalPosition.X.ShouldBe(3f, RoundTrip);
        first.LocalPosition.X.ShouldBe(1.5f, RoundTrip);
    }

    [Fact]
    public void A_snapped_axis_drag_leaves_the_other_axes_untouched()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        var start = new Vector3(0f, 0.37f, -1.62f);
        SceneNode node = harness.AddSelectedNode(start);
        float length = harness.GeometryAt(start).AxisLength;

        harness.Grab(start + Vector3.UnitX * (length * AlongAxis));
        harness.DragBy(Vector3.UnitX * 5.4f);
        harness.Release();

        node.LocalPosition.X.ShouldBe(5f, RoundTrip);
        node.LocalPosition.Y.ShouldBe(start.Y);
        node.LocalPosition.Z.ShouldBe(start.Z);
    }

    [Fact]
    public void A_finer_preset_gives_finer_landings()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        harness.Translate.Snap.Increment = 0.25f;
        SceneNode node = harness.AddSelectedNode(Vector3.Zero);
        float length = harness.GeometryAt(Vector3.Zero).AxisLength;

        harness.Grab(Vector3.UnitX * (length * AlongAxis));
        harness.DragBy(Vector3.UnitX * 4.2f);
        harness.Release();

        node.LocalPosition.X.ShouldBe(4.25f, RoundTrip);
    }

    [Fact]
    public void Holding_the_toggle_modifier_drops_snapping_mid_drag()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode node = harness.AddSelectedNode(Vector3.Zero);
        float length = harness.GeometryAt(Vector3.Zero).AxisLength;

        harness.Grab(Vector3.UnitX * (length * AlongAxis));

        harness.DragBy(Vector3.UnitX * 4.2f);
        node.LocalPosition.X.ShouldBe(4f, RoundTrip);

        harness.DragBy(Vector3.UnitX * 4.2f, KeyModifiers.Alt);
        node.LocalPosition.X.ShouldBe(4.2f, RoundTrip);

        harness.DragBy(Vector3.UnitX * 4.2f);
        node.LocalPosition.X.ShouldBe(4f, RoundTrip);

        harness.Release();
        harness.Translate.Snap.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void A_snapped_plane_drag_quantizes_the_movement_in_both_of_its_axes()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        var start = new Vector3(0.3f, 0.4f, 2f);
        SceneNode node = harness.AddSelectedNode(start);

        GizmoGeometry geometry = harness.GeometryAt(start);
        geometry.TryGetPlaneQuad(GizmoHandle.PlaneXY, out Vector3 corner, out Vector3 u, out Vector3 v, out float size)
            .ShouldBeTrue();

        harness.Grab(corner + (u + v) * (size * 0.5f));
        harness.Gizmo.ActiveHandle.ShouldBe(GizmoHandle.PlaneXY);
        harness.DragBy(new Vector3(3.7f, 5.4f, 0f));
        harness.Release();

        // The movement rounds to (4, 5); the start's offsets ride along.
        node.LocalPosition.X.ShouldBe(4.3f, RoundTrip);
        node.LocalPosition.Y.ShouldBe(5.4f, RoundTrip);
        node.LocalPosition.Z.ShouldBe(start.Z, RoundTrip);
    }

    [Theory]
    [InlineData(GizmoHandle.Screen)]
    [InlineData(GizmoHandle.AxisX)]
    public void A_held_frame_that_never_moved_the_cursor_leaves_an_off_grid_selection_alone(GizmoHandle handle)
    {
        // A plain click is press, held frames, release. It must not snap an
        // off-grid object onto the grid.
        var harness = GizmoHarness.ThreeQuarterView();
        var start = new Vector3(3.7f, 2.2f, -0.4f);
        SceneNode node = harness.AddSelectedNode(start);
        harness.Translate.Snap.Enabled.ShouldBeTrue();

        Vector3 aim = handle == GizmoHandle.Screen
            ? start
            : start + Vector3.UnitX * (harness.GeometryAt(start).AxisLength * AlongAxis);

        harness.Grab(aim).ShouldBe(GizmoUpdateResult.DragBegan);
        harness.Gizmo.ActiveHandle.ShouldBe(handle);

        harness.DragBy(Vector3.Zero).ShouldBe(GizmoUpdateResult.DragUpdated);
        node.LocalPosition.ShouldBe(start);
        harness.Translate.DragDelta.ShouldBe(Vector3.Zero);

        // A gesture that changed nothing cancels.
        harness.Release().ShouldBe(GizmoUpdateResult.DragCancelled);
        node.LocalPosition.ShouldBe(start);
        harness.Undo.Count.ShouldBe(0);
    }

    [Fact]
    public void The_grid_still_takes_effect_on_the_first_frame_the_cursor_actually_moves()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        var start = new Vector3(3.7f, 2.2f, -0.4f);
        SceneNode node = harness.AddSelectedNode(start);

        harness.Grab(start + Vector3.UnitX * (harness.GeometryAt(start).AxisLength * AlongAxis));
        harness.DragBy(Vector3.Zero);
        harness.DragBy(Vector3.UnitX * 1.4f);

        node.LocalPosition.X.ShouldBe(4.7f, RoundTrip);
        node.LocalPosition.Y.ShouldBe(start.Y, RoundTrip);
        node.LocalPosition.Z.ShouldBe(start.Z, RoundTrip);

        harness.Release().ShouldBe(GizmoUpdateResult.DragCommitted);
        harness.Undo.Count.ShouldBe(1);
    }
}
