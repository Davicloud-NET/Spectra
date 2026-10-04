using SpectraEngine.Core.Input;
using System.Numerics;

namespace SpectraEngine.Editing.Input;

/// <summary>
/// One frame of input for the editing layer: cursor, viewport size, buttons and
/// their edges, modifiers, wheel, navigation axis and frame time.
/// </summary>
// Editor tools read this and nothing else, so a new host only has to build one.
// Nothing in this assembly may reference Silk.NET.
public readonly struct EditorInputFrame
{
    /// <summary>Creates a snapshot from already-gathered per-frame state.</summary>
    public EditorInputFrame(
        Vector2 cursorPosition,
        Vector2 viewportSize,
        PointerButtons buttonsDown,
        PointerButtons buttonsPressed,
        PointerButtons buttonsReleased,
        KeyModifiers modifiers,
        Vector2 scrollDelta,
        float deltaTime,
        Vector2 cursorDelta = default,
        EditorNavigationInput navigation = default,
        bool isCursorLocked = false)
    {
        CursorPosition = cursorPosition;
        ViewportSize = viewportSize;
        ButtonsDown = buttonsDown;
        ButtonsPressed = buttonsPressed;
        ButtonsReleased = buttonsReleased;
        Modifiers = modifiers;
        ScrollDelta = scrollDelta;
        DeltaTime = deltaTime;
        CursorDelta = cursorDelta;
        Navigation = navigation;
        IsCursorLocked = isCursorLocked;
    }

    /// <summary>
    /// Cursor position in viewport pixels, origin top-left, y down. Can lie
    /// outside the viewport, so a drag that runs off the edge keeps tracking.
    /// </summary>
    public Vector2 CursorPosition { get; }

    /// <summary>The viewport's size in pixels.</summary>
    public Vector2 ViewportSize { get; }

    /// <summary>Mouse buttons held down at snapshot time.</summary>
    public PointerButtons ButtonsDown { get; }

    /// <summary>Mouse buttons that went down on this frame.</summary>
    public PointerButtons ButtonsPressed { get; }

    /// <summary>
    /// Mouse buttons that went up on this frame. A button pressed and released
    /// inside one frame is in both edge sets.
    /// </summary>
    public PointerButtons ButtonsReleased { get; }

    /// <summary>Modifier keys held at snapshot time.</summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>
    /// Wheel movement over this frame in notches. Y is the vertical wheel,
    /// positive away from the user.
    /// </summary>
    public Vector2 ScrollDelta { get; }

    /// <summary>The frame's duration in seconds.</summary>
    public float DeltaTime { get; }

    /// <summary>
    /// Cursor motion over this frame in pixels. The only motion signal while
    /// the cursor is locked; a host that tracks none leaves it at zero.
    /// </summary>
    public Vector2 CursorDelta { get; }

    /// <summary>This frame's fly-camera axis, resolved by the host from its keymap.</summary>
    public EditorNavigationInput Navigation { get; }

    /// <summary>
    /// True while the cursor is captured for freelook. <see cref="CursorPosition"/>
    /// means nothing then; test <see cref="IsPointerUsable"/> before using it.
    /// </summary>
    public bool IsCursorLocked { get; }

    /// <summary>
    /// True when <see cref="CursorPosition"/> lies inside the viewport rect.
    /// False for a zero-sized viewport.
    /// </summary>
    public bool IsCursorInsideViewport =>
        CursorPosition.X >= 0f && CursorPosition.X < ViewportSize.X &&
        CursorPosition.Y >= 0f && CursorPosition.Y < ViewportSize.Y;

    /// <summary>
    /// True when <see cref="CursorPosition"/> can be used as a viewport
    /// coordinate: inside the viewport and not locked.
    /// </summary>
    public bool IsPointerUsable => !IsCursorLocked && IsCursorInsideViewport;

    /// <summary>
    /// True when every button in <paramref name="buttons"/> is held.
    /// <see cref="PointerButtons.None"/> returns false.
    /// </summary>
    public bool IsDown(PointerButtons buttons) => Matches(ButtonsDown, buttons);

    /// <summary>
    /// True when every button in <paramref name="buttons"/> went down on this
    /// frame. <see cref="PointerButtons.None"/> returns false.
    /// </summary>
    public bool WasPressed(PointerButtons buttons) => Matches(ButtonsPressed, buttons);

    /// <summary>
    /// True when every button in <paramref name="buttons"/> went up on this
    /// frame. <see cref="PointerButtons.None"/> returns false.
    /// </summary>
    public bool WasReleased(PointerButtons buttons) => Matches(ButtonsReleased, buttons);

    /// <summary>
    /// True when every modifier in <paramref name="modifiers"/> is held; others
    /// may be held too. <see cref="KeyModifiers.None"/> returns true.
    /// </summary>
    public bool HasModifiers(KeyModifiers modifiers) => (Modifiers & modifiers) == modifiers;

    // None never matches, so an unassigned binding is safe to test.
    private static bool Matches(PointerButtons state, PointerButtons query) =>
        query != PointerButtons.None && (state & query) == query;
}
