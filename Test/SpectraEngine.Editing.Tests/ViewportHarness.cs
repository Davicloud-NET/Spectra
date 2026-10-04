using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Undo;
using SpectraEngine.Editing.Viewport;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

// Headless viewport with the whole editing stack wired up, aimed in pixels.
// Camera damping is off so tests need not simulate the settle.
internal sealed class ViewportHarness
{
    public ViewportHarness(
        float viewportWidth = 800f, float viewportHeight = 600f, GizmoStyle? gizmoStyle = null)
    {
        ViewportSize = new Vector2(viewportWidth, viewportHeight);
        Scene = new Scene("Viewport");
        // The pipelines normally set this each frame. Without it projected aim
        // points land a few pixels off.
        Scene.Camera.AspectRatio = ViewportSize.X / ViewportSize.Y;
        Undo = new UndoStack(Scene);
        // Classic, not the Studio default: these tests aim at handles a fixed
        // distance from the pivot. Studio puts them on the selection's box.
        Gizmos = new GizmoController(Scene, Undo) { Style = gizmoStyle ?? GizmoStyle.Classic };
        CursorLock = new FakeCursorLock();
        EditorCamera = new EditorCameraController(Scene)
        {
            SmoothingTimeConstant = 0f,
            CursorLock = CursorLock,
        };
        Viewport = new ViewportInteractionController(Scene, Gizmos) { CameraController = EditorCamera };
    }

    // Requests only apply on Pump, like the real latch's main-thread round trip.
    public FakeCursorLock CursorLock { get; }

    public Scene Scene { get; }

    public UndoStack Undo { get; }

    public GizmoController Gizmos { get; }

    public EditorCameraController EditorCamera { get; }

    public ViewportInteractionController Viewport { get; }

    public Vector2 ViewportSize { get; }

    public Vector2 CenterPixel => ViewportSize * 0.5f;

    public ViewportHarness Orbit(Vector3 focus, float distance, float yaw, float pitch)
    {
        EditorCamera.SetOrbit(focus, distance, yaw, pitch);
        return this;
    }

    public SceneNode AddBrush(Vector3 position, float halfExtent = 0.5f, string name = "Brush")
    {
        SceneNode node = Scene.Root.CreateChild(name);
        node.LocalPosition = position;
        node.Brush = Brush.CreateBox(new Vector3(-halfExtent), new Vector3(halfExtent));
        return node;
    }

    public SceneNode AddSelectedBrush(Vector3 position, float halfExtent = 0.5f, string name = "Brush")
    {
        SceneNode node = AddBrush(position, halfExtent, name);
        Scene.Selection.Add(node);
        return node;
    }

    // Inverse of Camera.ScreenPointToRay's pixel mapping.
    public Vector2 WorldToScreen(Vector3 world)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), Scene.Camera.GetViewProjection());
        return new Vector2(
            (clip.X / clip.W + 1f) * 0.5f * ViewportSize.X,
            (1f - clip.Y / clip.W) * 0.5f * ViewportSize.Y);
    }

    // A locked frame repeats the same cursor and varies only cursorDelta,
    // as the real input source does.
    public EditorInputFrame Frame(
        Vector2 cursor,
        PointerButtons down = PointerButtons.None,
        PointerButtons pressed = PointerButtons.None,
        PointerButtons released = PointerButtons.None,
        KeyModifiers modifiers = KeyModifiers.None,
        Vector2 scroll = default,
        float deltaTime = 1f / 60f,
        Vector2 cursorDelta = default,
        EditorNavigationInput navigation = default,
        bool locked = false) =>
        new(cursor, ViewportSize, down, pressed, released, modifiers, scroll, deltaTime,
            cursorDelta, navigation, locked);

    public EditorInputFrame FrameAt(
        Vector3 aimAt,
        PointerButtons down = PointerButtons.None,
        PointerButtons pressed = PointerButtons.None,
        PointerButtons released = PointerButtons.None,
        KeyModifiers modifiers = KeyModifiers.None) =>
        Frame(WorldToScreen(aimAt), down, pressed, released, modifiers);

    public ViewportDragMode Move(Vector2 cursor) => Viewport.Update(Frame(cursor));

    public ViewportDragMode Press(Vector2 cursor, KeyModifiers modifiers = KeyModifiers.None) =>
        Viewport.Update(Frame(cursor, PointerButtons.Left, PointerButtons.Left, modifiers: modifiers));

    public ViewportDragMode Drag(Vector2 cursor, KeyModifiers modifiers = KeyModifiers.None) =>
        Viewport.Update(Frame(cursor, PointerButtons.Left, modifiers: modifiers));

    public ViewportDragMode Release(Vector2 cursor, KeyModifiers modifiers = KeyModifiers.None) =>
        Viewport.Update(Frame(cursor, released: PointerButtons.Left, modifiers: modifiers));

    public void DragRect(Vector2 from, Vector2 to, KeyModifiers modifiers = KeyModifiers.None)
    {
        Press(from, modifiers);
        Drag(to, modifiers);
        Release(to, modifiers);
    }
}
