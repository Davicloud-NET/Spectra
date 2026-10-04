using SpectraEngine.Core.Input;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Input;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>Right-drag freelook, fly keys, wheel speed trim and the cursor lock.</summary>
// Position under a look is compared for equality, not tolerance: rotating about
// a focus offset passes a tolerance at the origin and drifts far from it.
public sealed class EditorFreelookTests
{
    [Fact]
    public void Looking_turns_the_camera_without_moving_it()
    {
        var harness = new ViewportHarness();
        var eye = new Vector3(5f, 3f, -7f);
        harness.EditorCamera.SetPose(eye, 0.4f, -0.2f);

        LookDrag(harness, new Vector2(400f, 300f), new Vector2(460f, 340f));

        float sensitivity = harness.EditorCamera.LookSensitivity;
        harness.EditorCamera.Yaw.ShouldBe(0.4f + 60f * sensitivity, 1e-6f);
        harness.EditorCamera.Pitch.ShouldBe(-0.2f - 40f * sensitivity, 1e-6f);

        harness.Scene.Camera.Position.ShouldBe(eye);
        harness.EditorCamera.Position.ShouldBe(eye);
    }

    [Fact]
    public void Looking_leaves_the_camera_still_at_a_million_units_out()
    {
        var harness = new ViewportHarness();
        var distant = new Vector3(1_000_000f, 250f, -1_000_000f);
        harness.EditorCamera.SetPose(distant, 0.7f, -0.35f);

        harness.EditorCamera.Update(harness.Frame(
            new Vector2(400f, 300f), down: PointerButtons.Right, pressed: PointerButtons.Right));
        for (int i = 0; i < 100; i++)
        {
            harness.EditorCamera.Update(harness.Frame(
                new Vector2(400f + (i % 9), 300f + (i % 5)), down: PointerButtons.Right));
        }

        harness.Scene.Camera.Position.ShouldBe(distant);
        harness.Scene.Camera.Forward.Length().ShouldBe(1f, 1e-5f);
    }

    [Fact]
    public void Looking_carries_the_focus_around_with_the_camera()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(new Vector3(5f, 3f, -7f), 0.4f, -0.2f, distance: 14f);

        LookDrag(harness, new Vector2(400f, 300f), new Vector2(500f, 280f));

        Vector3 expected = harness.Scene.Camera.Position + harness.Scene.Camera.Forward * 14f;
        harness.EditorCamera.Focus.ShouldBeCloseTo(expected, 1e-3f);
        harness.EditorCamera.Distance.ShouldBe(14f);
    }

    [Fact]
    public void Looking_past_vertical_clamps_short_of_the_degenerate_pole()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(Vector3.Zero, 0f, 0f);

        // More than a quarter turn each way.
        LookDrag(harness, new Vector2(400f, 900f), new Vector2(400f, -900f));
        harness.EditorCamera.Pitch.ShouldBeLessThan(MathF.PI / 2f);
        harness.Scene.Camera.Pitch.ShouldBe(harness.EditorCamera.Pitch);
        harness.Scene.Camera.Forward.Length().ShouldBe(1f, 1e-5f);

        LookDrag(harness, new Vector2(400f, -900f), new Vector2(400f, 900f));
        harness.EditorCamera.Pitch.ShouldBeGreaterThan(-MathF.PI / 2f);
        harness.Scene.Camera.Forward.Length().ShouldBe(1f, 1e-5f);
    }

    [Fact]
    public void The_press_frame_of_a_look_applies_no_delta()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(Vector3.Zero, 0.3f, -0.2f);

        // Cursor travels a long way before the press.
        harness.EditorCamera.Update(harness.Frame(new Vector2(20f, 20f)));
        harness.EditorCamera.Update(harness.Frame(
            new Vector2(700f, 500f), down: PointerButtons.Right, pressed: PointerButtons.Right));

        harness.EditorCamera.Yaw.ShouldBe(0.3f);
        harness.EditorCamera.Pitch.ShouldBe(-0.2f);
    }

    [Fact]
    public void Pressing_alt_mid_look_restarts_the_gesture_rather_than_snapping()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(new Vector3(2f, 1f, 3f), 0.3f, -0.2f);

        harness.EditorCamera.Update(harness.Frame(
            new Vector2(400f, 300f), down: PointerButtons.Right, pressed: PointerButtons.Right));
        harness.EditorCamera.Update(harness.Frame(new Vector2(430f, 300f), down: PointerButtons.Right));
        float yaw = harness.EditorCamera.Yaw;

        // Alt goes down, cursor still.
        harness.EditorCamera.Update(harness.Frame(
            new Vector2(430f, 300f), down: PointerButtons.Right, modifiers: KeyModifiers.Alt));

        harness.EditorCamera.ActiveGesture.ShouldBe(EditorNavigationGesture.Orbit);
        harness.EditorCamera.Yaw.ShouldBe(yaw);
    }

    [Fact]
    public void The_forward_key_moves_the_camera_along_what_it_is_looking_at()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(new Vector3(1f, 2f, 3f), 0.6f, -0.25f);
        Vector3 start = harness.Scene.Camera.Position;
        Vector3 forward = harness.Scene.Camera.Forward;
        harness.EditorCamera.FlySpeed = 10f;

        Fly(harness, EditorNavigationInput.FromKeys(
            forward: true, back: false, left: false, right: false, up: false, down: false), 0.5f);

        harness.Scene.Camera.Position.ShouldBeCloseTo(start + forward * 5f, 1e-4f);
        harness.EditorCamera.Yaw.ShouldBe(0.6f);
        harness.EditorCamera.Pitch.ShouldBe(-0.25f);
    }

    [Theory]
    [InlineData(true, false, false, false, 1f, 0f)]   // D
    [InlineData(false, true, false, false, -1f, 0f)]  // A
    [InlineData(false, false, true, false, 0f, 1f)]   // W
    [InlineData(false, false, false, true, 0f, -1f)]  // S
    public void Each_movement_key_moves_along_its_own_camera_axis(
        bool right, bool left, bool forward, bool back, float rightAmount, float forwardAmount)
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(new Vector3(4f, -1f, 8f), 1.1f, -0.4f);
        Vector3 start = harness.Scene.Camera.Position;
        Vector3 cameraRight = harness.Scene.Camera.Right;
        Vector3 cameraForward = harness.Scene.Camera.Forward;
        harness.EditorCamera.FlySpeed = 8f;

        Fly(harness, EditorNavigationInput.FromKeys(forward, back, left, right, up: false, down: false), 0.25f);

        Vector3 expected = start + (cameraRight * rightAmount + cameraForward * forwardAmount) * (8f * 0.25f);
        harness.Scene.Camera.Position.ShouldBeCloseTo(expected, 1e-4f);
    }

    [Theory]
    [InlineData(true, false, 1f)]
    [InlineData(false, true, -1f)]
    public void The_rise_and_fall_keys_move_along_WORLD_up_however_the_camera_is_pitched(
        bool up, bool down, float sign)
    {
        // Pitched steeply so a camera-relative up would show.
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(new Vector3(3f, 5f, -2f), 0.9f, -1.2f);
        Vector3 start = harness.Scene.Camera.Position;
        harness.EditorCamera.FlySpeed = 6f;

        Fly(harness, EditorNavigationInput.FromKeys(
            forward: false, back: false, left: false, right: false, up: up, down: down), 0.5f);

        harness.Scene.Camera.Position.ShouldBeCloseTo(start + Vector3.UnitY * (sign * 3f), 1e-4f);
    }

    [Fact]
    public void Holding_two_keys_is_no_faster_than_holding_one()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(Vector3.Zero, 0f, 0f);
        harness.EditorCamera.FlySpeed = 10f;

        Fly(harness, EditorNavigationInput.FromKeys(
            forward: true, back: false, left: false, right: true, up: false, down: false), 0.5f);

        harness.Scene.Camera.Position.Length().ShouldBe(5f, 1e-4f);
    }

    [Fact]
    public void Opposing_keys_cancel_instead_of_jittering()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(Vector3.Zero, 0f, 0f);

        EditorNavigationInput both = EditorNavigationInput.FromKeys(
            forward: true, back: true, left: true, right: true, up: true, down: true);
        both.IsIdle.ShouldBeTrue();

        Fly(harness, both, 0.5f);
        harness.Scene.Camera.Position.ShouldBe(Vector3.Zero);
    }

    [Fact]
    public void The_boost_modifier_multiplies_the_distance_travelled()
    {
        var plain = new ViewportHarness();
        plain.EditorCamera.SetPose(Vector3.Zero, 0f, 0f);
        plain.EditorCamera.FlySpeed = 10f;
        Fly(plain, EditorNavigationInput.FromKeys(
            forward: true, back: false, left: false, right: false, up: false, down: false), 0.5f);

        var boosted = new ViewportHarness();
        boosted.EditorCamera.SetPose(Vector3.Zero, 0f, 0f);
        boosted.EditorCamera.FlySpeed = 10f;
        Fly(boosted, EditorNavigationInput.FromKeys(
            forward: true, back: false, left: false, right: false, up: false, down: false, boost: true), 0.5f);

        float multiplier = boosted.EditorCamera.BoostMultiplier;
        boosted.Scene.Camera.Position.Length()
            .ShouldBe(plain.Scene.Camera.Position.Length() * multiplier, 1e-3f);
    }

    [Fact]
    public void Flying_carries_the_focus_along_so_the_orbit_pivot_stays_ahead()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(new Vector3(1f, 2f, 3f), 0.6f, -0.25f, distance: 20f);
        Vector3 offset = harness.EditorCamera.Focus - harness.EditorCamera.Position;
        harness.EditorCamera.FlySpeed = 10f;

        Fly(harness, EditorNavigationInput.FromKeys(
            forward: true, back: false, left: false, right: false, up: false, down: false), 0.5f);

        (harness.EditorCamera.Focus - harness.EditorCamera.Position).ShouldBeCloseTo(offset, 1e-3f);
    }

    [Fact]
    public void The_wheel_while_looking_trims_the_fly_speed_and_does_not_zoom()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(Vector3.Zero, 0f, 0f, distance: 20f);
        harness.EditorCamera.FlySpeed = 10f;
        float step = harness.EditorCamera.FlySpeedStep;

        harness.EditorCamera.Update(harness.Frame(
            harness.CenterPixel, down: PointerButtons.Right, pressed: PointerButtons.Right));
        harness.EditorCamera.Update(harness.Frame(
            harness.CenterPixel, down: PointerButtons.Right, scroll: new Vector2(0f, 2f)));

        harness.EditorCamera.FlySpeed.ShouldBe(10f * step * step, 1e-3f);
        harness.EditorCamera.Distance.ShouldBe(20f);
    }

    [Fact]
    public void The_trimmed_fly_speed_persists_after_the_look_ends()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(Vector3.Zero, 0f, 0f);
        harness.EditorCamera.FlySpeed = 10f;

        harness.EditorCamera.Update(harness.Frame(
            harness.CenterPixel, down: PointerButtons.Right, pressed: PointerButtons.Right,
            scroll: new Vector2(0f, 3f)));
        float trimmed = harness.EditorCamera.FlySpeed;
        trimmed.ShouldBeGreaterThan(10f);

        harness.EditorCamera.Update(harness.Frame(harness.CenterPixel, released: PointerButtons.Right));
        harness.EditorCamera.SuspendNavigation();

        harness.EditorCamera.FlySpeed.ShouldBe(trimmed);
    }

    [Fact]
    public void The_wheel_without_the_look_button_still_zooms_and_leaves_the_speed_alone()
    {
        var harness = new ViewportHarness();
        harness.Orbit(Vector3.Zero, 20f, 0f, 0f);
        harness.EditorCamera.FlySpeed = 10f;

        harness.EditorCamera.Update(harness.Frame(harness.CenterPixel, scroll: new Vector2(0f, 1f)));

        harness.EditorCamera.Distance.ShouldBe(20f / harness.EditorCamera.ZoomStep, 1e-3f);
        harness.EditorCamera.FlySpeed.ShouldBe(10f);
    }

    [Fact]
    public void The_fly_speed_trim_is_clamped_at_both_ends()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.MinFlySpeed = 1f;
        harness.EditorCamera.MaxFlySpeed = 100f;
        harness.EditorCamera.FlySpeed = 10f;

        harness.EditorCamera.AdjustFlySpeed(500f);
        harness.EditorCamera.FlySpeed.ShouldBe(100f);

        harness.EditorCamera.AdjustFlySpeed(-500f);
        harness.EditorCamera.FlySpeed.ShouldBe(1f);
    }

    [Fact]
    public void A_locked_look_reads_relative_motion_and_ignores_the_frozen_position()
    {
        // A captured cursor reports a frozen position; only the delta moves.
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(Vector3.Zero, 0f, 0f);
        Vector2 frozen = harness.CenterPixel;

        harness.EditorCamera.Update(harness.Frame(
            frozen, down: PointerButtons.Right, pressed: PointerButtons.Right));
        // Lock lands: this frame's delta is the backend's teleport.
        harness.EditorCamera.Update(harness.Frame(
            frozen, down: PointerButtons.Right, cursorDelta: new Vector2(999f, 999f), locked: true));
        harness.EditorCamera.Yaw.ShouldBe(0f);

        harness.EditorCamera.Update(harness.Frame(
            frozen, down: PointerButtons.Right, cursorDelta: new Vector2(40f, -20f), locked: true));

        float sensitivity = harness.EditorCamera.LookSensitivity;
        harness.EditorCamera.Yaw.ShouldBe(40f * sensitivity, 1e-6f);
        harness.EditorCamera.Pitch.ShouldBe(20f * sensitivity, 1e-6f);
        harness.Scene.Camera.Position.ShouldBe(Vector3.Zero);
    }

    [Fact]
    public void Releasing_a_locked_look_does_not_read_the_restored_cursor_as_a_flick()
    {
        var harness = new ViewportHarness();
        harness.EditorCamera.SetPose(Vector3.Zero, 0f, 0f);

        harness.EditorCamera.Update(harness.Frame(
            new Vector2(400f, 300f), down: PointerButtons.Right, pressed: PointerButtons.Right));
        harness.EditorCamera.Update(harness.Frame(
            new Vector2(400f, 300f), down: PointerButtons.Right, cursorDelta: new Vector2(50f, 0f), locked: true));
        float yaw = harness.EditorCamera.Yaw;

        // Unlock teleports the pointer back to where the press happened.
        harness.EditorCamera.Update(harness.Frame(new Vector2(120f, 640f), released: PointerButtons.Right));

        harness.EditorCamera.Yaw.ShouldBe(yaw);
        harness.EditorCamera.ActiveGesture.ShouldBe(EditorNavigationGesture.None);
    }

    // The press frame applies no delta, so all travel lands on the second frame.
    private static void LookDrag(ViewportHarness harness, Vector2 from, Vector2 to)
    {
        harness.EditorCamera.Update(
            harness.Frame(from, down: PointerButtons.Right, pressed: PointerButtons.Right));
        harness.EditorCamera.Update(harness.Frame(to, down: PointerButtons.Right));
    }

    // Movement keys only count while the look button is held.
    private static void Fly(ViewportHarness harness, EditorNavigationInput navigation, float seconds)
    {
        harness.EditorCamera.Update(harness.Frame(
            harness.CenterPixel,
            down: PointerButtons.Right,
            pressed: PointerButtons.Right,
            deltaTime: seconds,
            navigation: navigation));
    }
}
