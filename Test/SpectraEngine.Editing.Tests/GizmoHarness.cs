using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Undo;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

// Headless viewport for driving a GizmoTool: scene, camera, undo, controller.
// Tests aim the cursor at world points, which WorldToScreen projects to pixels,
// so a drag can be stated in world units and checked to float precision.
internal sealed class GizmoHarness
{
    private Vector3 _grabAim;

    // Style defaults to Classic, not the engine default (Studio): Classic
    // handles sit at a fixed distance from the pivot, which tests can aim at.
    // For Studio, pass it and ask GrabPointFor where the handle is.
    public GizmoHarness(
        Vector3 cameraPosition,
        Vector3 lookAt,
        float viewportWidth = 800f,
        float viewportHeight = 600f,
        GizmoStyle? style = null)
    {
        ViewportSize = new Vector2(viewportWidth, viewportHeight);
        Scene = new Scene("Gizmo");
        Scene.Camera.Position = cameraPosition;
        Scene.Camera.LookAt(lookAt);
        // The pipelines normally set this each frame.
        Scene.Camera.AspectRatio = ViewportSize.X / ViewportSize.Y;
        Undo = new UndoStack(Scene);
        Gizmos = new GizmoController(Scene, Undo) { Style = style ?? GizmoStyle.Classic };
    }

    // All three axes are separated on screen.
    public static GizmoHarness ThreeQuarterView(GizmoStyle? style = null) =>
        new(new Vector3(8f, 6f, 10f), Vector3.Zero, style: style);

    // Looking down -z: x and y lie in the view plane, z projects to a point.
    public static GizmoHarness FrontView(float distance = 10f, GizmoStyle? style = null) =>
        new(new Vector3(0f, 0f, distance), Vector3.Zero, style: style);

    public Scene Scene { get; }

    public UndoStack Undo { get; }

    public GizmoController Gizmos { get; }

    public GizmoTool Gizmo => Gizmos.Active;

    public TranslateGizmo Translate => Gizmos.Translate;

    public RotateGizmo Rotate => Gizmos.Rotate;

    public ScaleGizmo Scale => Gizmos.Scale;

    public Vector2 ViewportSize { get; }

    public GizmoTool Use(GizmoMode mode)
    {
        Gizmos.Mode = mode;
        return Gizmos.Active;
    }

    public SceneNode AddSelectedNode(Vector3 position, string name = "Node")
    {
        SceneNode node = AddNode(position, name);
        Scene.Selection.Add(node);
        return node;
    }

    public SceneNode AddNode(Vector3 position, string name = "Node")
    {
        SceneNode node = Scene.Root.CreateChild(name);
        node.LocalPosition = position;
        return node;
    }

    public SceneNode AddSelectedBrushNode(Vector3 position, float halfExtent = 1f, string name = "Brush")
    {
        SceneNode node = AddNode(position, name);
        node.Brush = Brush.CreateBox(new Vector3(-halfExtent), new Vector3(halfExtent));
        Scene.Selection.Add(node);
        return node;
    }

    // A mesh node has a measurable size. A bare node has none, and the resize
    // tool falls back to a proportional drag for it.
    public SceneNode AddSelectedMeshNode(Vector3 position, float halfExtent = 0.5f, string name = "Mesh")
    {
        SceneNode node = AddMeshNode(position, halfExtent, name);
        Scene.Selection.Add(node);
        return node;
    }

    public SceneNode AddMeshNode(Vector3 position, float halfExtent = 0.5f, string name = "Mesh")
    {
        SceneNode node = AddNode(position, name);
        node.MeshRenderer = new MeshRenderer(BoxMesh.Centred(new Vector3(halfExtent)), new Material(null));
        return node;
    }

    // Inverse of Camera.ScreenPointToRay's pixel-to-NDC mapping.
    public Vector2 WorldToScreen(Vector3 world)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), Scene.Camera.GetViewProjection());
        return new Vector2(
            (clip.X / clip.W + 1f) * 0.5f * ViewportSize.X,
            (1f - clip.Y / clip.W) * 0.5f * ViewportSize.Y);
    }

    // Geometry for a world-aligned pivot, without running the state machine.
    public GizmoGeometry GeometryAt(Vector3 pivot) => GeometryAt(pivot, Quaternion.Identity);

    public GizmoGeometry GeometryAt(Vector3 pivot, Quaternion frame) =>
        GizmoGeometry.Build(Scene.Camera, pivot, frame, ViewportSize, Gizmo.HandlePixelSize);

    // What the live tool lays out for the current selection and style.
    public GizmoGeometry LiveGeometry() => Gizmo.GeometryFor(ViewportSize);

    // A world point on the handle to aim a grab at: the resize cube's centre,
    // or the middle of an axis shaft. Studio handles stand on the selection's
    // box, so their position cannot be hard-coded.
    public Vector3 GrabPointFor(GizmoHandle handle)
    {
        GizmoGeometry geometry = LiveGeometry();

        if (Gizmo.Mode == GizmoMode.Scale &&
            geometry.TryGetHandleBox(handle, out Vector3 boxCentre, out _))
        {
            return boxCentre;
        }

        if (geometry.TryGetAxisSegment(handle, out Vector3 start, out Vector3 end))
            return Vector3.Lerp(start, end, 0.5f);

        return geometry.Pivot;
    }

    public EditorInputFrame Frame(
        Vector2 cursor,
        PointerButtons down = PointerButtons.None,
        PointerButtons pressed = PointerButtons.None,
        PointerButtons released = PointerButtons.None,
        KeyModifiers modifiers = KeyModifiers.None) =>
        new(cursor, ViewportSize, down, pressed, released, modifiers, Vector2.Zero, 1f / 60f);

    public GizmoUpdateResult Hover(Vector3 aimAt) =>
        Gizmos.Update(Frame(WorldToScreen(aimAt)));

    public GizmoUpdateResult Grab(Vector3 aimAt)
    {
        _grabAim = aimAt;
        return Gizmos.Update(Frame(
            WorldToScreen(aimAt), down: PointerButtons.Left, pressed: PointerButtons.Left));
    }

    // The delta is from the grab point, not from the previous DragBy.
    public GizmoUpdateResult DragBy(Vector3 worldDelta, KeyModifiers modifiers = KeyModifiers.None) =>
        DragTo(_grabAim + worldDelta, modifiers);

    public GizmoUpdateResult DragTo(Vector3 aimAt, KeyModifiers modifiers = KeyModifiers.None) =>
        Gizmos.Update(Frame(
            WorldToScreen(aimAt), down: PointerButtons.Left, modifiers: modifiers));

    public GizmoUpdateResult Release(KeyModifiers modifiers = KeyModifiers.None) =>
        Gizmos.Update(Frame(
            WorldToScreen(_grabAim), released: PointerButtons.Left, modifiers: modifiers));

    public GizmoUpdateResult PressEscape() =>
        Gizmos.Update(Frame(WorldToScreen(_grabAim), down: PointerButtons.Left), cancelRequested: true);

    // Right button cancels a drag.
    public GizmoUpdateResult RightClick() =>
        Gizmos.Update(Frame(
            WorldToScreen(_grabAim),
            down: PointerButtons.Left | PointerButtons.Right,
            pressed: PointerButtons.Right));
}
