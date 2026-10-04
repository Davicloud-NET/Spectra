using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>Angle snapping: <see cref="AngleSnapSettings"/> arithmetic and a live rotate drag.</summary>
public sealed class GizmoAngleSnappingTests
{
    private const float Tolerance = 0.02f;

    [Fact]
    public void The_default_increment_is_fifteen_degrees_on_the_documented_ladder()
    {
        var snap = new AngleSnapSettings();

        snap.Increment.ShouldBe(AngleSnapSettings.DefaultIncrementDegrees);
        snap.Increment.ShouldBe(15f);
        snap.Enabled.ShouldBeTrue();
        snap.ToggleModifier.ShouldBe(KeyModifiers.Alt);
        AngleSnapSettings.Presets.ShouldBe(new[] { 5f, 15f, 45f, 90f });
    }

    [Theory]
    [InlineData(15f, 37f, 30f)]
    [InlineData(15f, 38f, 45f)]
    [InlineData(15f, -37f, -30f)]
    [InlineData(90f, 44f, 0f)]
    [InlineData(90f, 46f, 90f)]
    [InlineData(5f, 12.4f, 10f)]
    public void Snapping_rounds_degrees_to_the_nearest_increment(float increment, float value, float expected) =>
        new AngleSnapSettings { Increment = increment }.SnapScalar(value).ShouldBe(expected, 1e-4f);

    [Fact]
    public void Snapping_radians_rounds_in_degrees_and_comes_back_in_radians()
    {
        var snap = new AngleSnapSettings(); // 15°

        // 1.6 rad is 91.67°.
        snap.SnapRadians(1.6f).ShouldBe(90f * MathF.PI / 180f, 1e-6f);
        snap.SnapRadians(0.05f).ShouldBe(0f);
    }

    [Fact]
    public void The_preset_ladder_clamps_at_both_ends()
    {
        var snap = new AngleSnapSettings();

        snap.CyclePreset(-1).ShouldBe(5f);
        snap.CyclePreset(-1).ShouldBe(5f); // clamped, not wrapped
        snap.CyclePreset(1).ShouldBe(15f);
        snap.CyclePreset(5).ShouldBe(90f);
        snap.CyclePreset(1).ShouldBe(90f);

        // An off-ladder value enters at its nearest rung by ratio: 20 is nearer 15.
        snap.Increment = 20f;
        snap.CyclePreset(1).ShouldBe(45f);
    }

    [Fact]
    public void A_snapped_drag_lands_on_a_whole_increment()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode node = harness.AddSelectedNode(Vector3.Zero);
        RotateGizmo rotate = (RotateGizmo)harness.Use(GizmoMode.Rotate);
        rotate.Snap.Enabled = true;
        rotate.Snap.Increment = 15f;

        RotateGizmoDragTests.Sweep(harness, GizmoHandle.AxisY, 37f * MathF.PI / 180f);

        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 30f * MathF.PI / 180f);
        RotateGizmoDragTests.ShouldRotateLike(node.LocalRotation, expected);
    }

    [Fact]
    public void The_modifier_frees_a_rotation_from_the_increment_for_one_gesture()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode node = harness.AddSelectedNode(Vector3.Zero);
        RotateGizmo rotate = (RotateGizmo)harness.Use(GizmoMode.Rotate);
        rotate.Snap.Enabled = true;

        RotateGizmoDragTests.Sweep(harness, GizmoHandle.AxisY, 37f * MathF.PI / 180f, KeyModifiers.Alt);

        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 37f * MathF.PI / 180f);
        RotateGizmoDragTests.ShouldRotateLike(node.LocalRotation, expected);

        rotate.Snap.Enabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData(5f, 37f, 35f)]
    [InlineData(45f, 37f, 45f)]
    [InlineData(90f, 37f, 0f)]    // less than half a rung
    [InlineData(90f, 100f, 90f)]
    public void Each_preset_increment_quantises_a_sweep_to_its_own_ladder(
        float increment, float sweepDegrees, float expectedDegrees)
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode node = harness.AddSelectedNode(Vector3.Zero);
        RotateGizmo rotate = (RotateGizmo)harness.Use(GizmoMode.Rotate);
        rotate.Snap.Increment = increment;

        RotateGizmoDragTests.Sweep(harness, GizmoHandle.AxisY, sweepDegrees * MathF.PI / 180f);

        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, expectedDegrees * MathF.PI / 180f);
        RotateGizmoDragTests.ShouldRotateLike(node.LocalRotation, expected);
    }

    [Fact]
    public void A_sweep_shorter_than_half_an_increment_snaps_back_to_nothing_and_commits_nothing()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode node = harness.AddSelectedNode(Vector3.Zero);
        RotateGizmo rotate = (RotateGizmo)harness.Use(GizmoMode.Rotate);
        rotate.Snap.Increment = 15f;

        RotateGizmoDragTests.Sweep(harness, GizmoHandle.AxisZ, 4f * MathF.PI / 180f);

        node.LocalRotation.ShouldBe(Quaternion.Identity);
        harness.Undo.Count.ShouldBe(0);
    }

    [Fact]
    public void The_snap_toggle_moves_all_three_tools_together()
    {
        var harness = GizmoHarness.ThreeQuarterView();

        harness.Gizmos.SnapEnabled.ShouldBeTrue();
        harness.Gizmos.Apply(GizmoCommand.ToggleSnap).ShouldBeTrue();

        harness.Translate.Snap.Enabled.ShouldBeFalse();
        harness.Rotate.Snap.Enabled.ShouldBeFalse();
        harness.Scale.Snap.Enabled.ShouldBeFalse();

        // Increments are per tool; FinerSnap steps all three.
        harness.Gizmos.Apply(GizmoCommand.FinerSnap).ShouldBeTrue();
        harness.Translate.Snap.Increment.ShouldBe(0.5f);
        harness.Rotate.Snap.Increment.ShouldBe(5f);
        harness.Scale.Snap.Increment.ShouldBe(0.5f, Tolerance);
    }
}
