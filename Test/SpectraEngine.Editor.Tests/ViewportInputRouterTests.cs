using SpectraEngine.Core.Input;
using SpectraEngine.Editor.Viewport;

namespace SpectraEngine.Editor.Tests;

public sealed class ViewportInputRouterTests
{
    private const int ViewportWidth = 800;
    private const int ViewportHeight = 600;
    private const int OriginX = 100;
    private const int OriginY = 50;
    private const int AnchorScreenX = OriginX + (ViewportWidth / 2);
    private const int AnchorScreenY = OriginY + (ViewportHeight / 2);

    // Half of SM_CXDRAG at 100% scaling.
    private const int Slack = 4;

    private sealed class FakeCursor : IViewportCursor
    {
        internal List<string> Calls { get; } = [];

        internal ViewportSize Size { get; set; } = new(ViewportWidth, ViewportHeight);

        internal ViewportPoint Origin { get; set; } = new(OriginX, OriginY);

        internal int Slack { get; set; } = ViewportInputRouterTests.Slack;

        internal ViewportPoint Position { get; private set; }

        internal bool Clipped { get; private set; }

        internal bool Hidden { get; private set; }

        internal bool Captured { get; private set; }

        public ViewportSize ClientSize => Size;

        public int DragSlack => Slack;

        public ViewportPoint ClientToScreen(ViewportPoint client) =>
            new(client.X + Origin.X, client.Y + Origin.Y);

        public void MoveCursor(int screenX, int screenY)
        {
            Position = new ViewportPoint(screenX, screenY);
            Calls.Add($"move {screenX},{screenY}");
        }

        public void ClipToClient(bool clip)
        {
            Clipped = clip;
            Calls.Add(clip ? "clip" : "unclip");
        }

        public void SetCursorHidden(bool hidden)
        {
            Hidden = hidden;
            Calls.Add(hidden ? "hide" : "show");
        }

        public void SetPointerCapture(bool captured)
        {
            Captured = captured;
            Calls.Add(captured ? "capture" : "release");
        }
    }

    private sealed class RecordingSink : IInputSink
    {
        internal List<InputEvent> Events { get; } = [];

        public void Submit(in InputEvent input) => Events.Add(input);

        // Reduces the stream the way InputManager does. FocusLost counts as a
        // release of everything, so the router must not also send a button-up
        // per held button.
        internal PointerButtons ReplayHeldButtons()
        {
            PointerButtons held = PointerButtons.None;

            foreach (InputEvent input in Events)
            {
                switch (input.Kind)
                {
                    case InputEventKind.PointerDown: held |= input.Button; break;
                    case InputEventKind.PointerUp: held &= ~input.Button; break;
                    case InputEventKind.FocusLost: held = PointerButtons.None; break;
                }
            }

            return held;
        }
    }

    private readonly FakeCursor _cursor = new();
    private readonly RecordingSink _sink = new();
    private readonly List<ShellChord> _chords = [];
    private readonly List<ViewportPoint> _menus = [];
    private readonly ViewportInputRouter _router;

    public ViewportInputRouterTests()
    {
        _router = new ViewportInputRouter(_cursor) { Sink = _sink };
        _router.ShellChord += chord => _chords.Add(chord);
        _router.ContextMenuRequested += (x, y) => _menus.Add(new ViewportPoint(x, y));
    }

    [Fact]
    public void The_lock_pins_the_cursor_at_the_centre_of_the_viewport()
    {
        _router.OnPointerMove(10, 20);
        _router.ApplyCursorMode(CursorMode.Locked);

        _router.IsCursorLocked.ShouldBeTrue();
        _cursor.Position.ShouldBe(new ViewportPoint(AnchorScreenX, AnchorScreenY));
        _cursor.Hidden.ShouldBeTrue();
        _cursor.Clipped.ShouldBeTrue();
        _cursor.Captured.ShouldBeTrue();
    }

    [Fact]
    public void A_locked_move_is_measured_against_the_anchor_and_re_pinned()
    {
        _router.ApplyCursorMode(CursorMode.Locked);
        _sink.Events.Clear();

        _router.OnPointerMove((ViewportWidth / 2) + 12, (ViewportHeight / 2) - 5);

        InputEvent submitted = _sink.Events.ShouldHaveSingleItem();
        submitted.Kind.ShouldBe(InputEventKind.PointerDelta);
        submitted.Value.X.ShouldBe(12f);
        submitted.Value.Y.ShouldBe(-5f);

        _cursor.Position.ShouldBe(new ViewportPoint(AnchorScreenX, AnchorScreenY));
    }

    [Fact]
    public void The_re_pin_echo_is_not_reported_as_motion()
    {
        // Moving the cursor generates its own move message, a zero delta.
        _router.ApplyCursorMode(CursorMode.Locked);
        _sink.Events.Clear();

        _router.OnPointerMove(ViewportWidth / 2, ViewportHeight / 2);

        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Unlocking_releases_the_clip_strictly_before_the_restore_teleport()
    {
        // If the teleport ran first, a restore point outside the clip rect
        // would be clamped onto its edge.
        _router.OnPointerMove(20, 30);
        _router.ApplyCursorMode(CursorMode.Locked);
        _router.ApplyCursorMode(CursorMode.Normal);

        int unclip = _cursor.Calls.IndexOf("unclip");
        int restore = _cursor.Calls.IndexOf($"move {20 + OriginX},{30 + OriginY}");

        unclip.ShouldBeGreaterThanOrEqualTo(0);
        restore.ShouldBeGreaterThanOrEqualTo(0);
        unclip.ShouldBeLessThan(restore);
    }

    [Fact]
    public void Unlocking_restores_the_press_point_rather_than_the_anchor()
    {
        _router.OnPointerMove(20, 30);
        _router.ApplyCursorMode(CursorMode.Locked);
        _router.ApplyCursorMode(CursorMode.Normal);

        _cursor.Position.ShouldBe(new ViewportPoint(20 + OriginX, 30 + OriginY));
        _cursor.Hidden.ShouldBeFalse();
        _cursor.Clipped.ShouldBeFalse();
        _router.IsCursorLocked.ShouldBeFalse();
    }

    [Fact]
    public void A_viewport_that_moves_under_a_live_lock_re_anchors()
    {
        // The anchor is a screen point. Left stale, the next move would report
        // the pane's whole displacement as one frame of look.
        _router.ApplyCursorMode(CursorMode.Locked);
        _cursor.Origin = new ViewportPoint(OriginX + 300, OriginY + 40);
        _router.OnViewportMoved();

        _cursor.Position.ShouldBe(new ViewportPoint(AnchorScreenX + 300, AnchorScreenY + 40));

        _sink.Events.Clear();
        _router.OnPointerMove((ViewportWidth / 2) + 7, ViewportHeight / 2);

        InputEvent submitted = _sink.Events.ShouldHaveSingleItem();
        submitted.Value.X.ShouldBe(7f);
        submitted.Value.Y.ShouldBe(0f);
    }

    [Fact]
    public void Focus_loss_ends_the_lock_and_leaves_no_button_held()
    {
        _router.OnPointerMove(40, 40);
        _router.OnPointerDown(PointerButtons.Left);
        _router.OnPointerDown(PointerButtons.Right);
        _router.ApplyCursorMode(CursorMode.Locked);

        _router.OnFocusLost();

        _router.IsCursorLocked.ShouldBeFalse();
        _router.ButtonsDown.ShouldBe(PointerButtons.None);
        _cursor.Clipped.ShouldBeFalse();
        _cursor.Hidden.ShouldBeFalse();

        _sink.Events[^1].Kind.ShouldBe(InputEventKind.FocusLost);
        _sink.ReplayHeldButtons().ShouldBe(PointerButtons.None);
    }

    [Fact]
    public void A_lost_capture_ends_the_gesture_without_dropping_the_keyboard()
    {
        _router.OnPointerMove(40, 40);
        _router.OnPointerDown(PointerButtons.Left);
        _router.OnPointerDown(PointerButtons.Middle);

        _router.OnPointerCaptureLost();

        _router.ButtonsDown.ShouldBe(PointerButtons.None);
        _sink.ReplayHeldButtons().ShouldBe(PointerButtons.None);

        // FocusLost would also drop the movement keys of a live freelook.
        _sink.Events.ShouldNotContain(input => input.Kind == InputEventKind.FocusLost);
    }

    [Fact]
    public void A_lost_capture_opens_no_context_menu()
    {
        _router.OnPointerMove(30, 30);
        _router.OnPointerDown(PointerButtons.Right);

        _router.OnPointerCaptureLost();

        _menus.ShouldBeEmpty();
        _router.ButtonsDown.ShouldBe(PointerButtons.None);
    }

    [Fact]
    public void A_lost_capture_with_nothing_held_is_nothing()
    {
        // Releasing a capture raises the loss, so every normal gesture end
        // arrives here too.
        _router.OnPointerMove(30, 30);
        _router.OnPointerDown(PointerButtons.Left);
        _router.OnPointerUp(PointerButtons.Left);

        int before = _sink.Events.Count;
        _router.OnPointerCaptureLost();

        _sink.Events.Count.ShouldBe(before);
    }

    [Fact]
    public void No_button_is_left_down_across_a_lock_transition()
    {
        _router.OnPointerMove(50, 50);
        _router.OnPointerDown(PointerButtons.Right);
        _router.ApplyCursorMode(CursorMode.Locked);
        _router.OnPointerMove((ViewportWidth / 2) + 20, ViewportHeight / 2);
        _router.ApplyCursorMode(CursorMode.Normal);
        _router.OnPointerUp(PointerButtons.Right);

        _router.ButtonsDown.ShouldBe(PointerButtons.None);
        _sink.ReplayHeldButtons().ShouldBe(PointerButtons.None);
        _cursor.Captured.ShouldBeFalse();
    }

    [Fact]
    public void A_hesitant_click_inside_the_drag_slack_opens_the_context_menu()
    {
        _router.OnPointerMove(200, 150);
        _router.OnPointerDown(PointerButtons.Right);
        _router.OnPointerMove(202, 150);
        _router.OnPointerMove(201, 151);
        _router.OnPointerMove(204, 150);
        _router.OnPointerUp(PointerButtons.Right);

        _menus.ShouldHaveSingleItem().ShouldBe(new ViewportPoint(200, 150));
    }

    [Fact]
    public void A_press_that_leaves_the_drag_slack_never_opens_the_menu()
    {
        // Coming back inside the slack does not make it a click again.
        _router.OnPointerMove(200, 150);
        _router.OnPointerDown(PointerButtons.Right);
        _router.OnPointerMove(205 + Slack, 150);
        _router.OnPointerMove(200, 150);
        _router.OnPointerUp(PointerButtons.Right);

        _menus.ShouldBeEmpty();
    }

    [Fact]
    public void Travel_accumulates_across_both_move_shapes()
    {
        // Three pixels as a position plus three as a locked delta: inside the
        // slack separately, outside it together.
        _router.OnPointerMove(200, 150);
        _router.OnPointerDown(PointerButtons.Right);
        _router.OnPointerMove(203, 150);
        _router.ApplyCursorMode(CursorMode.Locked);
        _router.OnPointerMove((ViewportWidth / 2) + 3, ViewportHeight / 2);
        _router.OnPointerUp(PointerButtons.Right);

        _menus.ShouldBeEmpty();
    }

    [Fact]
    public void The_arbitration_is_net_displacement_rather_than_path_length()
    {
        // Four pixels out and four back: eight of path, zero displacement.
        _router.OnPointerMove(200, 150);
        _router.OnPointerDown(PointerButtons.Right);
        _router.OnPointerMove(200 + Slack, 150);
        _router.ApplyCursorMode(CursorMode.Locked);
        _router.OnPointerMove((ViewportWidth / 2) - Slack, ViewportHeight / 2);
        _router.OnPointerUp(PointerButtons.Right);

        _menus.ShouldHaveSingleItem().ShouldBe(new ViewportPoint(200, 150));
    }

    [Fact]
    public void The_slack_is_read_from_the_host_at_every_press()
    {
        // DPI is per window and can change between presses.
        _cursor.Slack = 20;

        _router.OnPointerMove(200, 150);
        _router.OnPointerDown(PointerButtons.Right);
        _router.OnPointerMove(215, 150);
        _router.OnPointerUp(PointerButtons.Right);

        _menus.ShouldHaveSingleItem().ShouldBe(new ViewportPoint(200, 150));
    }

    [Fact]
    public void A_left_press_is_never_a_context_menu()
    {
        _router.OnPointerMove(200, 150);
        _router.OnPointerDown(PointerButtons.Left);
        _router.OnPointerUp(PointerButtons.Left);

        _menus.ShouldBeEmpty();
    }

    [Fact]
    public void The_menu_opens_after_the_engine_has_seen_the_release()
    {
        InputEventKind lastBeforeMenu = InputEventKind.FocusLost;
        _router.ContextMenuRequested += (_, _) => lastBeforeMenu = _sink.Events[^1].Kind;

        _router.OnPointerMove(200, 150);
        _router.OnPointerDown(PointerButtons.Right);
        _router.OnPointerUp(PointerButtons.Right);

        lastBeforeMenu.ShouldBe(InputEventKind.PointerUp);
    }

    [Theory]
    [InlineData(InputKey.N, ShellChord.NewMap)]
    [InlineData(InputKey.O, ShellChord.OpenMap)]
    [InlineData(InputKey.S, ShellChord.SaveMap)]
    [InlineData(InputKey.Number1, ShellChord.InsertBlock)]
    [InlineData(InputKey.Number2, ShellChord.InsertPart)]
    [InlineData(InputKey.Number3, ShellChord.InsertCut)]
    [InlineData(InputKey.Number4, ShellChord.InsertLight)]
    public void A_chord_in_the_table_is_claimed_and_kept_from_the_engine(InputKey key, ShellChord expected)
    {
        _router.OnKeyDown(key, KeyModifiers.Control).ShouldBeTrue();

        _chords.ShouldHaveSingleItem().ShouldBe(expected);
        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public void Shift_selects_save_as()
    {
        _router.OnKeyDown(InputKey.S, KeyModifiers.Control | KeyModifiers.Shift).ShouldBeTrue();

        _chords.ShouldHaveSingleItem().ShouldBe(ShellChord.SaveMapAs);
    }

    [Theory]
    [InlineData(InputKey.W)]
    [InlineData(InputKey.Number5)]
    [InlineData(InputKey.Z)]
    public void A_key_outside_the_table_reaches_the_engine_even_with_control(InputKey key)
    {
        _router.OnKeyDown(key, KeyModifiers.Control).ShouldBeFalse();

        _chords.ShouldBeEmpty();
        _sink.Events.ShouldHaveSingleItem().Key.ShouldBe(key);
    }

    [Fact]
    public void F11_is_claimed_without_a_modifier()
    {
        _router.OnKeyDown(InputKey.F11, KeyModifiers.None).ShouldBeTrue();

        _chords.ShouldHaveSingleItem().ShouldBe(ShellChord.MaximiseViewport);
        _sink.Events.ShouldBeEmpty();
    }

    [Fact]
    public void The_drawer_chord_needs_control_and_the_bare_key_does_not()
    {
        // A bare backtick already shows the console.
        _router.OnKeyDown(InputKey.GraveAccent, KeyModifiers.None).ShouldBeFalse();
        _chords.ShouldBeEmpty();

        _router.OnKeyDown(InputKey.GraveAccent, KeyModifiers.Control).ShouldBeTrue();
        _chords.ShouldHaveSingleItem().ShouldBe(ShellChord.ToggleBottomDrawer);
    }

    [Fact]
    public void F11_during_a_freelook_goes_to_the_engine_like_every_other_chord()
    {
        _router.ApplyCursorMode(CursorMode.Locked);

        _router.OnKeyDown(InputKey.F11, KeyModifiers.None).ShouldBeFalse();
        _chords.ShouldBeEmpty();
    }

    [Fact]
    public void A_chord_key_without_control_is_just_a_key()
    {
        _router.OnKeyDown(InputKey.S, KeyModifiers.None).ShouldBeFalse();

        _chords.ShouldBeEmpty();
        _sink.Events.ShouldHaveSingleItem().Key.ShouldBe(InputKey.S);
    }

    [Fact]
    public void A_document_chord_during_a_freelook_goes_to_the_engine()
    {
        // During a look Ctrl is descend and S is backwards, not a save.
        _router.ApplyCursorMode(CursorMode.Locked);

        _router.OnKeyDown(InputKey.S, KeyModifiers.Control).ShouldBeFalse();

        _chords.ShouldBeEmpty();
        _sink.Events.ShouldHaveSingleItem().Key.ShouldBe(InputKey.S);
    }

    [Fact]
    public void The_chords_come_back_when_the_look_ends()
    {
        _router.ApplyCursorMode(CursorMode.Locked);
        _router.OnKeyDown(InputKey.S, KeyModifiers.Control);
        _router.ApplyCursorMode(CursorMode.Normal);

        _router.OnKeyDown(InputKey.S, KeyModifiers.Control).ShouldBeTrue();

        _chords.ShouldHaveSingleItem().ShouldBe(ShellChord.SaveMap);
    }

    [Fact]
    public void Alt_is_claimed_so_the_window_menu_does_not_eat_the_next_key()
    {
        // Claimed from the platform only; the engine still gets the key.
        _router.OnKeyDown(InputKey.AltLeft, KeyModifiers.Alt).ShouldBeTrue();
        _router.OnKeyDown(InputKey.A, KeyModifiers.Alt).ShouldBeTrue();

        _sink.Events.Count.ShouldBe(2);
        _sink.Events[0].Key.ShouldBe(InputKey.AltLeft);
        _sink.Events[1].Key.ShouldBe(InputKey.A);
    }

    [Fact]
    public void A_key_with_no_alt_and_no_chord_is_left_to_the_platform()
    {
        _router.OnKeyDown(InputKey.A, KeyModifiers.None).ShouldBeFalse();
    }

    [Fact]
    public void A_key_release_always_reaches_the_engine()
    {
        _router.OnKeyUp(InputKey.S);

        InputEvent submitted = _sink.Events.ShouldHaveSingleItem();
        submitted.Kind.ShouldBe(InputEventKind.KeyUp);
        submitted.Key.ShouldBe(InputKey.S);
    }

    [Fact]
    public void The_arbitration_runs_before_a_sink_exists()
    {
        _router.Sink = null;

        _router.OnPointerMove(200, 150);
        _router.OnPointerDown(PointerButtons.Right);
        _router.OnPointerUp(PointerButtons.Right);

        _menus.ShouldHaveSingleItem().ShouldBe(new ViewportPoint(200, 150));
        _router.ButtonsDown.ShouldBe(PointerButtons.None);
    }
}
