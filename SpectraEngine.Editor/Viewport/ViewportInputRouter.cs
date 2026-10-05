using SpectraEngine.Core.Input;
using System;
using System.Numerics;

namespace SpectraEngine.Editor.Viewport;

// The viewport's input arbitration, with no Win32 or Avalonia type in it so
// both hosts share it and tests can reach it. UI thread only.
// State is tracked even with no sink; input before the engine exists is dropped.
internal sealed class ViewportInputRouter
{
    // SM_CXDRAG at 100%. A press replaces it before any movement accumulates.
    private const int DefaultDragSlack = 4;

    private readonly IViewportCursor _cursor;

    private PointerButtons _buttonsDown;
    private bool _cursorLocked;

    // Screen point the cursor is pinned to while locked, and the client point
    // it returns to afterwards.
    private ViewportPoint _lockAnchor;
    private ViewportPoint _lockRestore;

    // The lock pins at the viewport centre, so this is what the restore uses.
    private ViewportPoint _lastClientPosition;

    // Right click vs right drag. The engine's freelook owns a right drag, so a
    // context menu is a right press that ended before it became one.
    // Net displacement from the press, not path length (SM_CXDRAG works the
    // same way): a slow shaky click is still a click.
    // Fed by both move shapes, since the lock turns positions into deltas.
    private ViewportPoint _rightPressPosition;
    private int _rightTravelX;
    private int _rightTravelY;
    private bool _rightPressActive;
    private bool _rightBecameDrag;

    private int _rightClickSlack = DefaultDragSlack;

    internal ViewportInputRouter(IViewportCursor cursor) => _cursor = cursor;

    // Null until the engine exists.
    internal IInputSink? Sink { get; set; }

    // Every chord raised here is one the engine no longer sees. Keep the list
    // to document verbs; scene chords stay with the engine keymap.
    internal event Action<ShellChord>? ShellChord;

    // A right click, in client pixels. The engine has already had the balanced
    // down/up pair.
    internal event Action<int, int>? ContextMenuRequested;

    internal bool IsCursorLocked => _cursorLocked;

    internal PointerButtons ButtonsDown => _buttonsDown;

    private static ShellChord? ShellChordFor(InputKey key, KeyModifiers modifiers)
    {
        // Letters and digits need Control or a movement key would fire a
        // dialog. F11 does not.
        bool control = (modifiers & KeyModifiers.Control) != 0;

        return key switch
        {
            InputKey.F11 => Viewport.ShellChord.MaximiseViewport,
            InputKey.GraveAccent when control => Viewport.ShellChord.ToggleBottomDrawer,

            InputKey.N when control => Viewport.ShellChord.NewMap,
            InputKey.O when control => Viewport.ShellChord.OpenMap,
            InputKey.S when control => (modifiers & KeyModifiers.Shift) != 0
                ? Viewport.ShellChord.SaveMapAs
                : Viewport.ShellChord.SaveMap,

        // Number row only. The keypad stays with the engine.
            InputKey.Number1 when control => Viewport.ShellChord.InsertBlock,
            InputKey.Number2 when control => Viewport.ShellChord.InsertPart,
            InputKey.Number3 when control => Viewport.ShellChord.InsertCut,
            InputKey.Number4 when control => Viewport.ShellChord.InsertLight,

            InputKey.P when control => Viewport.ShellChord.OpenPalette,
            InputKey.L when control => Viewport.ShellChord.ToggleLogicView,

            _ => null,
        };
    }

    // Absolute client position. While locked it is differenced against the
    // anchor and sent on as a delta.
    internal void OnPointerMove(int clientX, int clientY)
    {
        if (_cursorLocked)
        {
            ViewportPoint screen = _cursor.ClientToScreen(new ViewportPoint(clientX, clientY));

            int dx = screen.X - _lockAnchor.X;
            int dy = screen.Y - _lockAnchor.Y;

            // The re-pin below echoes back as a zero delta.
            if (dx == 0 && dy == 0)
                return;

            AccumulateRightTravel(dx, dy);
            Submit(InputEvent.PointerDelta(new Vector2(dx, dy)));
            _cursor.MoveCursor(_lockAnchor.X, _lockAnchor.Y);
            return;
        }

        var position = new ViewportPoint(clientX, clientY);
        AccumulateRightTravel(position.X - _lastClientPosition.X, position.Y - _lastClientPosition.Y);
        _lastClientPosition = position;

        Submit(InputEvent.PointerMove(new Vector2(position.X, position.Y)));
    }

    // Raw motion with no absolute position.
    internal void OnPointerDelta(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
            return;

        AccumulateRightTravel(dx, dy);
        Submit(InputEvent.PointerDelta(new Vector2(dx, dy)));
    }

    internal void OnPointerDown(PointerButtons button)
    {
        if (_buttonsDown is PointerButtons.None)
            _cursor.SetPointerCapture(true);

        _buttonsDown |= button;

        if (button == PointerButtons.Right)
        {
            _rightPressActive = true;
            _rightBecameDrag = false;
            _rightTravelX = 0;
            _rightTravelY = 0;
            _rightPressPosition = _lastClientPosition;
            _rightClickSlack = _cursor.DragSlack;
        }

        Submit(InputEvent.PointerDown(button));
    }

    internal void OnPointerUp(PointerButtons button)
    {
        _buttonsDown &= ~button;

        // Capture is held until the last button is up. The lock keeps its own.
        if (_buttonsDown is PointerButtons.None && !_cursorLocked)
            _cursor.SetPointerCapture(false);

        Submit(InputEvent.PointerUp(button));

        // After the up is submitted, so the freelook has ended before the
        // shell opens a menu.
        if (button == PointerButtons.Right && _rightPressActive)
        {
            _rightPressActive = false;
            if (!_rightBecameDrag)
                ContextMenuRequested?.Invoke(_rightPressPosition.X, _rightPressPosition.Y);
        }
    }

    // In notches.
    internal void OnScroll(float x, float y) =>
        Submit(InputEvent.Scroll(new Vector2(x, y)));

    // One-way: coming back inside the slack does not turn a drag into a click.
    private void AccumulateRightTravel(int dx, int dy)
    {
        if (!_rightPressActive || _rightBecameDrag)
            return;

        _rightTravelX += dx;
        _rightTravelY += dy;

        if (Math.Abs(_rightTravelX) > _rightClickSlack || Math.Abs(_rightTravelY) > _rightClickSlack)
            _rightBecameDrag = true;
    }

    // Returns whether the router claimed the key, so the host can keep it from
    // the platform.
    // No shell chords while locked: Ctrl is descend and S flies backwards, so
    // Ctrl+S would fire mid-flight. The console key is the exception. Neither
    // the fly camera nor the character binds it, and without it nothing can
    // be typed while a level plays.
    // Alt is claimed or the platform opens its window menu and eats the next key.
    internal bool OnKeyDown(InputKey key, KeyModifiers modifiers)
    {
        if (key == InputKey.GraveAccent && (modifiers & KeyModifiers.Control) == 0)
        {
            ShellChord?.Invoke(Viewport.ShellChord.ShowConsole);
            return true;
        }

        if (!_cursorLocked && ShellChordFor(key, modifiers) is { } chord)
        {
            ShellChord?.Invoke(chord);
            return true;
        }

        Submit(InputEvent.KeyDown(key));

        // Check the key too: a host may report the modifiers as they were
        // before this key.
        return (modifiers & KeyModifiers.Alt) != 0
            || key is InputKey.AltLeft or InputKey.AltRight;
    }

    internal void OnKeyUp(InputKey key) => Submit(InputEvent.KeyUp(key));

    // FocusLost releases everything on the engine side, so no per-button ups
    // here: they would arm the release edges twice.
    internal void OnFocusLost()
    {
        EndCursorLock();
        _buttonsDown = PointerButtons.None;
        _rightPressActive = false;
        Submit(InputEvent.FocusLost());
    }

    // Capture lost with focus kept. Not a focus loss: the keyboard is still
    // here, so held keys stay down and each held button gets one up.
    // The right press is cancelled, not resolved: no context menu.
    internal void OnPointerCaptureLost()
    {
        _rightPressActive = false;

        PointerButtons held = _buttonsDown;
        if (held is PointerButtons.None)
            return;

        _buttonsDown = PointerButtons.None;

        if ((held & PointerButtons.Left) != 0)
            Submit(InputEvent.PointerUp(PointerButtons.Left));
        if ((held & PointerButtons.Right) != 0)
            Submit(InputEvent.PointerUp(PointerButtons.Right));
        if ((held & PointerButtons.Middle) != 0)
            Submit(InputEvent.PointerUp(PointerButtons.Middle));
    }

    internal void ApplyCursorMode(CursorMode mode)
    {
        bool wanted = mode == CursorMode.Locked;
        if (wanted == _cursorLocked)
            return;

        if (wanted)
            BeginCursorLock();
        else
            EndCursorLock();
    }

    // The anchor is a screen point. If the pane moves under a live lock, the
    // next move would report the whole displacement as look, so re-anchor.
    internal void OnViewportMoved()
    {
        if (!_cursorLocked)
            return;

        _lockAnchor = ClientCentreOnScreen();
        _cursor.MoveCursor(_lockAnchor.X, _lockAnchor.Y);
        _cursor.ClipToClient(true);
    }

    private void BeginCursorLock()
    {
        if (_cursorLocked)
            return;

        // Pin at the centre: at an edge, half the travel leaves the window
        // before the re-pin catches it.
        _lockRestore = _lastClientPosition;
        _lockAnchor = ClientCentreOnScreen();

        _cursorLocked = true;
        _cursor.SetPointerCapture(true);
        _cursor.SetCursorHidden(true);
        _cursor.MoveCursor(_lockAnchor.X, _lockAnchor.Y);

        // Fence the hidden pointer so it cannot drift onto another monitor or
        // app. Released in EndCursorLock.
        _cursor.ClipToClient(true);
    }

    private void EndCursorLock()
    {
        if (!_cursorLocked)
            return;

        _cursorLocked = false;

        // Unclip before the restore move, or the fence clamps it.
        _cursor.ClipToClient(false);

        ViewportPoint restore = _cursor.ClientToScreen(_lockRestore);
        _cursor.MoveCursor(restore.X, restore.Y);
        _cursor.SetCursorHidden(false);

        if (_buttonsDown is PointerButtons.None)
            _cursor.SetPointerCapture(false);
    }

    private ViewportPoint ClientCentreOnScreen()
    {
        ViewportSize size = _cursor.ClientSize;
        return _cursor.ClientToScreen(new ViewportPoint(size.Width / 2, size.Height / 2));
    }

    private void Submit(in InputEvent input) => Sink?.Submit(in input);
}
