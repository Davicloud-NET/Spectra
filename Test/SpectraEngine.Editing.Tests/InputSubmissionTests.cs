using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Silk.NET.Input;
using SpectraEngine.Core.Input;
using System;
using System.Numerics;
using Xunit;
using CursorMode = SpectraEngine.Core.Input.CursorMode;

namespace SpectraEngine.Editing.Tests;

/// <summary>What a host submits, and what the input state machine makes of it.</summary>
public sealed class InputSubmissionTests
{
    private static InputManager CreateInput() => new(NullLogger<InputManager>.Instance);

    [Fact]
    public void A_submitted_key_is_held_until_it_is_released()
    {
        InputManager input = CreateInput();

        input.Submit(InputEvent.KeyDown(InputKey.W));
        input.IsKeyDown(InputKey.W).ShouldBeTrue();

        input.Submit(InputEvent.KeyUp(InputKey.W));
        input.IsKeyDown(InputKey.W).ShouldBeFalse();
    }

    [Fact]
    public void A_press_edge_lasts_exactly_one_frame()
    {
        InputManager input = CreateInput();

        input.Submit(InputEvent.KeyDown(InputKey.G));
        input.Update(0.016);
        input.WasKeyPressed(InputKey.G).ShouldBeTrue();

        input.Update(0.016);
        input.WasKeyPressed(InputKey.G).ShouldBeFalse("the edge is not the state");
        input.IsKeyDown(InputKey.G).ShouldBeTrue("but the key is still held");
    }

    [Fact]
    public void Auto_repeat_does_not_re_arm_the_press_edge()
    {
        InputManager input = CreateInput();

        input.Submit(InputEvent.KeyDown(InputKey.R));
        input.Update(0.016);
        input.WasKeyPressed(InputKey.R).ShouldBeTrue();

        input.Submit(InputEvent.KeyDown(InputKey.R));
        input.Update(0.016);
        input.WasKeyPressed(InputKey.R).ShouldBeFalse();
    }

    [Fact]
    public void An_unnameable_key_is_dropped_rather_than_held()
    {
        // All unnamed keys arrive as Unknown; held, they would share one entry.
        InputManager input = CreateInput();

        input.Submit(InputEvent.KeyDown(InputKey.Unknown));
        input.Update(0.016);

        input.IsKeyDown(InputKey.Unknown).ShouldBeFalse();
        input.WasKeyPressed(InputKey.Unknown).ShouldBeFalse();
    }

    [Fact]
    public void A_button_press_and_release_report_on_their_own_frames()
    {
        InputManager input = CreateInput();

        input.Submit(InputEvent.PointerDown(PointerButtons.Left));
        input.Update(0.016);
        input.PointerButtonsDown.ShouldBe(PointerButtons.Left);
        input.PointerButtonsPressed.ShouldBe(PointerButtons.Left);
        input.PointerButtonsReleased.ShouldBe(PointerButtons.None);

        input.Submit(InputEvent.PointerUp(PointerButtons.Left));
        input.Update(0.016);
        input.PointerButtonsDown.ShouldBe(PointerButtons.None);
        input.PointerButtonsPressed.ShouldBe(PointerButtons.None);
        input.PointerButtonsReleased.ShouldBe(PointerButtons.Left);
    }

    [Fact]
    public void A_button_already_down_does_not_re_arm_the_press_edge()
    {
        InputManager input = CreateInput();

        input.Submit(InputEvent.PointerDown(PointerButtons.Right));
        input.Update(0.016);
        input.PointerButtonsPressed.ShouldBe(PointerButtons.Right);

        input.Submit(InputEvent.PointerDown(PointerButtons.Right));
        input.Update(0.016);
        input.PointerButtonsPressed.ShouldBe(PointerButtons.None);
        input.PointerButtonsDown.ShouldBe(PointerButtons.Right);
    }

    [Fact]
    public void A_release_for_a_button_that_was_never_down_reports_nothing()
    {
        // Happens for real: press in another control, release over the viewport.
        InputManager input = CreateInput();

        input.Submit(InputEvent.PointerUp(PointerButtons.Middle));
        input.Update(0.016);

        input.PointerButtonsReleased.ShouldBe(PointerButtons.None);
    }

    [Fact]
    public void Two_buttons_are_tracked_independently()
    {
        InputManager input = CreateInput();

        input.Submit(InputEvent.PointerDown(PointerButtons.Left));
        input.Submit(InputEvent.PointerDown(PointerButtons.Right));
        input.Update(0.016);
        input.PointerButtonsDown.ShouldBe(PointerButtons.Left | PointerButtons.Right);

        input.Submit(InputEvent.PointerUp(PointerButtons.Left));
        input.Update(0.016);
        input.PointerButtonsDown.ShouldBe(PointerButtons.Right);
        input.PointerButtonsReleased.ShouldBe(PointerButtons.Left);
    }

    [Fact]
    public void An_absolute_move_reports_the_position_and_differences_the_motion()
    {
        InputManager input = CreateInput();

        input.Submit(InputEvent.PointerMove(new Vector2(100f, 50f)));
        input.Update(0.016);
        input.MousePosition.ShouldBe(new Vector2(100f, 50f));

        input.Submit(InputEvent.PointerMove(new Vector2(130f, 40f)));
        input.Update(0.016);
        input.MousePosition.ShouldBe(new Vector2(130f, 40f));
        input.MouseDelta.ShouldBe(new Vector2(30f, -10f));
    }

    [Fact]
    public void The_first_absolute_move_produces_no_motion()
    {
        InputManager input = CreateInput();

        input.Submit(InputEvent.PointerMove(new Vector2(640f, 360f)));
        input.Update(0.016);

        input.MouseDelta.ShouldBe(Vector2.Zero);
    }

    [Fact]
    public void A_raw_delta_moves_the_camera_without_moving_the_reported_position()
    {
        // Captured cursor: the frozen position is where the cursor is restored to.
        InputManager input = CreateInput();

        input.Submit(InputEvent.PointerMove(new Vector2(200f, 200f)));
        input.Update(0.016);

        input.Submit(InputEvent.PointerDelta(new Vector2(15f, -4f)));
        input.Submit(InputEvent.PointerDelta(new Vector2(5f, -1f)));
        input.Update(0.016);

        input.MouseDelta.ShouldBe(new Vector2(20f, -5f), "raw motion accumulates within a frame");
        input.MousePosition.ShouldBe(new Vector2(200f, 200f), "and leaves the position alone");
    }

    [Fact]
    public void Scroll_accumulates_within_a_frame_and_clears_after_it()
    {
        InputManager input = CreateInput();

        input.Submit(InputEvent.Scroll(new Vector2(0f, 1f)));
        input.Submit(InputEvent.Scroll(new Vector2(0f, 2f)));
        input.Update(0.016);
        input.ScrollDelta.ShouldBe(new Vector2(0f, 3f));

        input.Update(0.016);
        input.ScrollDelta.ShouldBe(Vector2.Zero);
    }

    [Fact]
    public void A_submitted_focus_loss_releases_everything_the_window_event_would()
    {
        InputManager input = CreateInput();
        input.Submit(InputEvent.KeyDown(InputKey.ShiftLeft));
        input.Submit(InputEvent.PointerDown(PointerButtons.Right));
        input.Update(0.016);

        input.Submit(InputEvent.FocusLost());
        input.Update(0.016);

        input.IsKeyDown(InputKey.ShiftLeft).ShouldBeFalse();
        input.PointerButtonsDown.ShouldBe(PointerButtons.None);
        input.PointerButtonsReleased.ShouldBe(
            PointerButtons.Right, "a held button becomes a release edge so the gesture watching for it ends");
    }

    [Fact]
    public void A_focus_loss_gives_the_cursor_back()
    {
        InputManager input = CreateInput();
        input.RequestCursorMode(CursorMode.Locked);
        input.ApplyPendingCursorMode();
        input.IsCursorLocked.ShouldBeTrue();

        input.Submit(InputEvent.FocusLost());

        input.IsCursorLocked.ShouldBeFalse();
        input.RequestedCursorMode.ShouldBe(
            CursorMode.Normal, "the pending request is overwritten too, or the next pump re-takes the lock");
    }

    [Fact]
    public void The_window_and_a_host_reach_the_same_state_machine()
    {
        InputManager viaWindow = CreateInput();
        InputManager viaHost = CreateInput();

        viaWindow.OnKeyDown(null!, Key.W, 0);
        viaWindow.OnMouseDown(null!, MouseButton.Right);
        viaWindow.OnMouseMove(null!, new Vector2(10f, 10f));
        viaWindow.OnMouseMove(null!, new Vector2(12f, 7f));
        viaWindow.OnScroll(null!, new ScrollWheel(0f, 1f));
        viaWindow.Update(0.016);

        viaHost.Submit(InputEvent.KeyDown(InputKey.W));
        viaHost.Submit(InputEvent.PointerDown(PointerButtons.Right));
        viaHost.Submit(InputEvent.PointerMove(new Vector2(10f, 10f)));
        viaHost.Submit(InputEvent.PointerMove(new Vector2(12f, 7f)));
        viaHost.Submit(InputEvent.Scroll(new Vector2(0f, 1f)));
        viaHost.Update(0.016);

        viaHost.IsKeyDown(InputKey.W).ShouldBe(viaWindow.IsKeyDown(InputKey.W));
        viaHost.WasKeyPressed(InputKey.W).ShouldBe(viaWindow.WasKeyPressed(InputKey.W));
        viaHost.PointerButtonsDown.ShouldBe(viaWindow.PointerButtonsDown);
        viaHost.PointerButtonsPressed.ShouldBe(viaWindow.PointerButtonsPressed);
        viaHost.MousePosition.ShouldBe(viaWindow.MousePosition);
        viaHost.MouseDelta.ShouldBe(viaWindow.MouseDelta);
        viaHost.ScrollDelta.ShouldBe(viaWindow.ScrollDelta);
    }

    [Fact]
    public void Every_engine_key_has_the_silk_key_of_the_same_name()
    {
        // Both enums spell their members the same, so the names check the
        // hand-written table.
        foreach (InputKey key in Enum.GetValues<InputKey>())
        {
            if (key is InputKey.Unknown)
                continue;

            Enum.TryParse(key.ToString(), out Key silk).ShouldBeTrue(
                $"InputKey.{key} has no Silk.NET key of the same name");
            SilkInputKeys.ToInputKey(silk).ShouldBe(key);
        }
    }

    [Fact]
    public void A_silk_key_the_engine_does_not_name_becomes_unknown()
    {
        SilkInputKeys.ToInputKey(Key.F20).ShouldBe(InputKey.Unknown);
        SilkInputKeys.ToInputKey(Key.KeypadEqual).ShouldBe(InputKey.Unknown);
    }

    [Fact]
    public void The_keypad_digits_are_their_own_keys_and_not_the_number_row()
    {
        // Views bind the keypad, tools the number row.
        SilkInputKeys.ToInputKey(Key.Keypad7).ShouldBe(InputKey.Keypad7);
        SilkInputKeys.ToInputKey(Key.Keypad7).ShouldNotBe(InputKey.Number7);
        SilkInputKeys.ToInputKey(Key.Keypad0).ShouldBe(InputKey.Keypad0);
        SilkInputKeys.ToInputKey(Key.Keypad9).ShouldBe(InputKey.Keypad9);
    }

    [Fact]
    public void The_three_mouse_buttons_map_and_the_rest_do_not()
    {
        SilkInputKeys.ToPointerButton(MouseButton.Left).ShouldBe(PointerButtons.Left);
        SilkInputKeys.ToPointerButton(MouseButton.Right).ShouldBe(PointerButtons.Right);
        SilkInputKeys.ToPointerButton(MouseButton.Middle).ShouldBe(PointerButtons.Middle);
        SilkInputKeys.ToPointerButton(MouseButton.Button4).ShouldBe(PointerButtons.None);
    }
}
