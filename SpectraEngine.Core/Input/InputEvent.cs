using System.Numerics;

namespace SpectraEngine.Core.Input;

/// <summary>What an <see cref="InputEvent"/> reports.</summary>
public enum InputEventKind
{
    /// <summary>A key went from up to down. Auto-repeat is filtered by the engine.</summary>
    KeyDown,

    /// <summary>A key went from down to up.</summary>
    KeyUp,

    /// <summary>
    /// The pointer moved to an absolute position in viewport pixels; the engine
    /// derives the motion from the previous one.
    /// </summary>
    PointerMove,

    /// <summary>
    /// The pointer moved by a raw delta, with no meaningful absolute position.
    /// What a captured (locked) cursor produces.
    /// </summary>
    PointerDelta,

    /// <summary>A pointer button went down.</summary>
    PointerDown,

    /// <summary>A pointer button went up.</summary>
    PointerUp,

    /// <summary>The wheel turned, in notches.</summary>
    Scroll,

    /// <summary>
    /// The viewport lost input focus: everything held is released and any
    /// cursor capture is given back.
    /// </summary>
    FocusLost,
}

/// <summary>
/// One piece of input, submitted by whoever owns the window. See
/// <c>EngineHost.SubmitInput</c>.
/// </summary>
// PointerMove and PointerDelta stay separate kinds: a captured cursor has no
// meaningful absolute position, only motion.
public readonly record struct InputEvent
{
    /// <summary>What this event reports.</summary>
    public InputEventKind Kind { get; private init; }

    /// <summary>The key, for <see cref="InputEventKind.KeyDown"/> and <see cref="InputEventKind.KeyUp"/>.</summary>
    public InputKey Key { get; private init; }

    /// <summary>
    /// The button, for <see cref="InputEventKind.PointerDown"/> and
    /// <see cref="InputEventKind.PointerUp"/>. One flag per event.
    /// </summary>
    public PointerButtons Button { get; private init; }

    /// <summary>
    /// Viewport pixels for <see cref="InputEventKind.PointerMove"/>, raw motion
    /// for <see cref="InputEventKind.PointerDelta"/>, and wheel notches for
    /// <see cref="InputEventKind.Scroll"/>.
    /// </summary>
    public Vector2 Value { get; private init; }

    /// <summary>A key going down.</summary>
    public static InputEvent KeyDown(InputKey key) =>
        new() { Kind = InputEventKind.KeyDown, Key = key };

    /// <summary>A key coming up.</summary>
    public static InputEvent KeyUp(InputKey key) =>
        new() { Kind = InputEventKind.KeyUp, Key = key };

    /// <summary>
    /// The pointer at an absolute position, in viewport pixels with the origin
    /// at the top-left and y growing downward.
    /// </summary>
    public static InputEvent PointerMove(Vector2 position) =>
        new() { Kind = InputEventKind.PointerMove, Value = position };

    /// <summary>Raw pointer motion, for a captured cursor.</summary>
    public static InputEvent PointerDelta(Vector2 delta) =>
        new() { Kind = InputEventKind.PointerDelta, Value = delta };

    /// <summary>A pointer button going down.</summary>
    public static InputEvent PointerDown(PointerButtons button) =>
        new() { Kind = InputEventKind.PointerDown, Button = button };

    /// <summary>A pointer button coming up.</summary>
    public static InputEvent PointerUp(PointerButtons button) =>
        new() { Kind = InputEventKind.PointerUp, Button = button };

    /// <summary>
    /// Wheel movement in notches: <c>Y</c> is the vertical wheel (positive is
    /// away from the user), <c>X</c> the horizontal one.
    /// </summary>
    public static InputEvent Scroll(Vector2 notches) =>
        new() { Kind = InputEventKind.Scroll, Value = notches };

    /// <summary>The viewport lost focus.</summary>
    public static InputEvent FocusLost() =>
        new() { Kind = InputEventKind.FocusLost };
}

/// <summary>
/// Where submitted input goes. Implemented by the engine's input manager.
/// </summary>
public interface IInputSink
{
    /// <summary>Applies one event to the engine's input state.</summary>
    void Submit(in InputEvent input);
}
