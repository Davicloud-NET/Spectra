using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Input;

/// <summary>
/// Builds each frame's <see cref="EditorInputFrame"/> from the engine's
/// <see cref="InputManager"/> and the renderer's framebuffer size.
/// </summary>
// Render thread only, after InputManager.Update has latched the frame.
// Viewport size comes from the renderer's latch, not IWindow: GLFW answers
// size queries only on the thread that created the window.
public sealed class EngineEditorInputSource : IEditorInputSource
{
    private readonly InputManager _input;
    private readonly Renderer _renderer;

    /// <summary>Creates an input source over a live engine's input manager and renderer.</summary>
    public EngineEditorInputSource(InputManager input, Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(renderer);
        _input = input;
        _renderer = renderer;
    }

    /// <summary>Snapshots input with the whole framebuffer as the viewport.</summary>
    public EditorInputFrame CaptureFrame(float deltaTime, EditorNavigationInput navigation = default)
    {
        _renderer.GetFramebufferSize(out int width, out int height);
        return CaptureFrame(deltaTime, Vector2.Zero, new Vector2(width, height), navigation);
    }

    /// <summary>
    /// Snapshots input for a viewport covering part of the window.
    /// <paramref name="viewportOrigin"/> is its top-left corner in window client
    /// pixels; the cursor position is reported relative to it.
    /// </summary>
    public EditorInputFrame CaptureFrame(
        float deltaTime, Vector2 viewportOrigin, Vector2 viewportSize, EditorNavigationInput navigation = default) =>
        new(
            _input.MousePosition - viewportOrigin,
            viewportSize,
            _input.PointerButtonsDown,
            _input.PointerButtonsPressed,
            _input.PointerButtonsReleased,
            _input.Modifiers,
            _input.ScrollDelta,
            deltaTime,
            _input.MouseDelta,
            navigation,
            _input.IsCursorLocked);
}
