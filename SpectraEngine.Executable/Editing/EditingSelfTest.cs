using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Undo;
using SpectraEngine.Editing.Viewport;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Executable.Editing;

// Opt-in smoke gate: every few seconds it picks a known brush node, drags its
// x translate handle one unit with synthesized input, checks the static world
// recompiled, then undoes, redoes and undoes again. One PASS/FAIL line per run.
//
// Borrows the scene camera and selection for one frame and hands both back.
// Runs on its own undo stack and gizmo controller so the user's history and
// gestures are untouched. The undo has to wait for the compile carrying the
// move to land: a move undone before the next placement snapshot dirties nothing.
// Render thread only.
internal sealed class EditingSelfTest : IEditorFrameProbe
{
    // Seconds between runs, from the end of the previous one.
    internal const double IntervalSeconds = 5.0;

    private const float SyntheticFrameSeconds = 1f / 60f;

    // Several steps, so a tool that accumulated per-frame deltas would land
    // at 1.85 units instead of 1.
    private static readonly Vector3 DragDelta = new(1f, 0f, 0f);
    private static readonly float[] DragSteps = [0.25f, 0.6f, 1f];

    // Relative to the node. All three axes separate on screen, and the demo
    // keeps this region free of parts so nothing occludes the node.
    private static readonly Vector3 CameraOffset = new(2.5f, 1.6f, 3f);

    // World units. Covers the project/unproject round trip.
    private const float DeltaTolerance = 1e-3f;

    private const int MaxCompileWatchFrames = 120;

    private readonly ILogger<EditingSelfTest> _logger;
    private readonly Scene _scene;
    private readonly SceneNode _node;

    private readonly UndoStack _rigUndo;
    private readonly GizmoController _rigGizmos;
    private readonly ViewportInteractionController _rigViewport;

    private readonly List<SceneNode> _savedSelection = [];

    // The grabbed gizmo draws here so its vertices can be counted without
    // putting a second gizmo on screen.
    private readonly DebugDraw _drawProbe = new();

    private Stage _stage = Stage.Idle;
    private double _elapsed;
    private double _nextRunTime = IntervalSeconds;

    private Transform _restLocal;
    private Transform _committedLocal;
    private Vector3 _measuredDelta;
    private ChunkCoord _expectedCell;
    private IReadOnlyList<ChunkCoord> _dirtyBaseline = [];
    private int _compileCountAtLaunch;
    private int _observedDirtyCells;
    private int _compileWatchFrames;
    private int _gizmoVertices;
    private bool _sawDirtySet;

    // node: a brush node whose chunk cells nothing else in the scene dirties,
    // or the recompile check proves nothing.
    public EditingSelfTest(ILogger<EditingSelfTest> logger, Scene scene, SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(node);

        _logger = logger;
        _scene = scene;
        _node = node;

        _rigUndo = new UndoStack(scene);
        _rigGizmos = new GizmoController(scene, _rigUndo);
        _rigViewport = new ViewportInteractionController(scene, _rigGizmos);

        // With snapping on, a drag that applied 0.6 units would round to 1
        // and pass.
        _rigGizmos.Translate.Snap.Enabled = false;
    }

    // Call before the host runs the viewport for the frame. During the compile
    // watch the node sits one unit off its rest pose, and a gizmo grab that
    // captured that would commit a transform the user never authored.
    // viewportIdle false: no run starts, and a run in progress abandons itself.
    public void Update(double deltaTime, Vector2 viewportSize, bool viewportIdle)
    {
        _elapsed += deltaTime;

        if (_stage == Stage.Idle)
        {
            if (_elapsed < _nextRunTime || !viewportIdle ||
                viewportSize.X <= 0f || viewportSize.Y <= 0f)
            {
                return;
            }

            RunDrag(viewportSize);
            return;
        }

        if (!viewportIdle)
        {
            Abandon("a viewport gesture started");
            return;
        }

        switch (_stage)
        {
            case Stage.WatchCompile:
                WatchCompile();
                break;
            case Stage.Undo:
                RunUndo();
                break;
            case Stage.Redo:
                RunRedo();
                break;
            default:
                RunRestore();
                break;
        }
    }

    private void RunDrag(Vector2 viewportSize)
    {
        Camera camera = _scene.Camera;

        Vector3 savedPosition = camera.Position;
        float savedYaw = camera.Yaw;
        float savedPitch = camera.Pitch;
        float savedAspect = camera.AspectRatio;
        SaveSelection();

        _restLocal = _node.LocalTransform;
        _committedLocal = _restLocal;
        _measuredDelta = Vector3.Zero;
        _gizmoVertices = 0;

        Vector3 restWorld = _node.WorldPosition;
        _expectedCell = ChunkCoord.FromPosition(restWorld);

        try
        {
            // The frame after a resize the camera still has last frame's aspect,
            // and an aim point projected with it misses the handle.
            camera.AspectRatio = viewportSize.X / viewportSize.Y;
            camera.Position = restWorld + CameraOffset;
            camera.LookAt(restWorld);
            _scene.Selection.Clear();

            if (!TryDriveChain(viewportSize, restWorld))
                return;
        }
        finally
        {
            // Reset first: it cancels a half-open transaction on a failure path.
            _rigGizmos.Reset();
            RestoreSelection();
            camera.AspectRatio = savedAspect;
            camera.Position = savedPosition;
            camera.Yaw = savedYaw;
            camera.Pitch = savedPitch;
        }

        // The compile carrying this move launches at the end of this frame.
        _dirtyBaseline = _scene.LastCompileDirtyCells;
        _compileWatchFrames = 0;
        _sawDirtySet = false;
        _observedDirtyCells = 0;
        _stage = Stage.WatchCompile;
    }

    // Returns false having already reported the failure.
    private bool TryDriveChain(Vector2 viewportSize, Vector3 restWorld)
    {
        Camera camera = _scene.Camera;

        Vector2 nodePixel = WorldToScreen(restWorld, viewportSize);
        Ray3 pickRay = camera.ScreenPointToRay(nodePixel, viewportSize);
        if (!_scene.Raycast(in pickRay, out SceneRaycastHit hit))
            return Fail("pick", "the screen ray through the node's own pixel hit nothing");
        if (!ReferenceEquals(hit.Node, _node))
            return Fail("pick", $"the screen ray hit '{hit.Node.Name}' instead of the target node");

        // A press/release pair through the viewport controller, so the drag
        // arbitration runs too.
        _rigViewport.Update(Frame(nodePixel, viewportSize, down: PointerButtons.Left, pressed: PointerButtons.Left));
        _rigViewport.Update(Frame(nodePixel, viewportSize, released: PointerButtons.Left));

        if (_scene.Selection.Count != 1 || !_scene.Selection.Contains(_node))
            return Fail("pick", "the click did not leave the target node as the sole selection");
        if (_rigUndo.UndoCount != 0)
            return Fail("pick", "a click that moved nothing landed an entry in the history");

        // Grab the x handle at the middle of its shaft. The geometry comes from
        // the tool because where a handle stands depends on the gizmo style.
        GizmoTool tool = _rigGizmos.Translate;
        GizmoGeometry geometry = tool.GeometryFor(viewportSize);
        if (geometry.IsBehindCamera)
            return Fail("grab", "the gizmo pivot projected behind the borrowed camera");

        if (!geometry.TryGetAxisSegment(GizmoHandle.AxisX, out Vector3 axisStart, out Vector3 axisEnd))
            return Fail("grab", "the live gizmo style offers no +x handle to grab");

        Vector3 grabAim = Vector3.Lerp(axisStart, axisEnd, 0.5f);

        _rigViewport.Update(Frame(
            WorldToScreen(grabAim, viewportSize), viewportSize,
            down: PointerButtons.Left, pressed: PointerButtons.Left));

        if (_rigViewport.DragMode != ViewportDragMode.Manipulate)
            return Fail("grab", $"the press on the x handle arbitrated as {_rigViewport.DragMode}, not Manipulate");
        if (tool.ActiveHandle != GizmoHandle.AxisX)
            return Fail("grab", $"the press grabbed {tool.ActiveHandle}, not the x axis handle");

        // Closest a headless run gets to "is the gizmo on screen".
        _drawProbe.Clear();
        tool.Draw(_drawProbe);
        _gizmoVertices = _drawProbe.VertexCount;
        if (_gizmoVertices == 0)
            return Fail("grab", "the grabbed manipulator drew no line geometry");

        // One input frame per waypoint, as absolute cursor positions.
        for (int i = 0; i < DragSteps.Length; i++)
        {
            _rigViewport.Update(Frame(
                WorldToScreen(grabAim + DragDelta * DragSteps[i], viewportSize), viewportSize,
                down: PointerButtons.Left));
        }

        _rigViewport.Update(Frame(
            WorldToScreen(grabAim + DragDelta, viewportSize), viewportSize,
            released: PointerButtons.Left));

        _measuredDelta = _node.WorldPosition - restWorld;
        _committedLocal = _node.LocalTransform;

        float error = Vector3.Distance(_measuredDelta, DragDelta);
        if (error > DeltaTolerance)
            return Fail("commit", $"the committed move is off by {error:0.000000} world units");
        if (_rigUndo.UndoCount != 1)
            return Fail("commit", $"the gesture landed {_rigUndo.UndoCount} history entries, not exactly one");
        if (_rigUndo.RedoCount != 0)
            return Fail("commit", "the gesture left redoable entries behind");

        return true;
    }

    private void WatchCompile()
    {
        // The dirty-cell list is replaced at every compile launch, so a new
        // reference means a launch, and a launch drains every pending cell.
        if (!_sawDirtySet && !ReferenceEquals(_scene.LastCompileDirtyCells, _dirtyBaseline))
        {
            IReadOnlyList<ChunkCoord> dirty = _scene.LastCompileDirtyCells;
            _sawDirtySet = true;
            _observedDirtyCells = dirty.Count;
            _compileCountAtLaunch = _scene.StaticWorldCompileCount;

            if (!Contains(dirty, _expectedCell))
            {
                Fail("recompile",
                    $"the compile launched after the drag dirtied {dirty.Count} cell(s), " +
                    $"none of them ({_expectedCell.X}, {_expectedCell.Y}, {_expectedCell.Z})");
                return;
            }
        }

        // One compile in flight at a time, so the next landing is this launch's.
        if (_sawDirtySet && _scene.StaticWorldCompileCount > _compileCountAtLaunch)
        {
            _stage = Stage.Undo;
            return;
        }

        if (++_compileWatchFrames > MaxCompileWatchFrames)
        {
            Fail("recompile",
                _sawDirtySet
                    ? $"the compile covering ({_expectedCell.X}, {_expectedCell.Y}, {_expectedCell.Z}) " +
                      $"never landed within {MaxCompileWatchFrames} frames"
                    : $"no static-world compile was launched within {MaxCompileWatchFrames} frames of the drag");
        }
    }

    private void RunUndo()
    {
        if (!_rigUndo.Undo())
        {
            Fail("undo", "the history refused to undo the committed drag");
            return;
        }

        // Exact, no tolerance: commands replay absolute transforms.
        if (!SameTransform(_node.LocalTransform, _restLocal))
        {
            Fail("undo", "undo did not restore the node's transform exactly");
            return;
        }

        _stage = Stage.Redo;
    }

    private void RunRedo()
    {
        if (!_rigUndo.Redo())
        {
            Fail("redo", "the history refused to redo the undone drag");
            return;
        }

        if (!SameTransform(_node.LocalTransform, _committedLocal))
        {
            Fail("redo", "redo did not re-apply the committed transform exactly");
            return;
        }

        _stage = Stage.Restore;
    }

    private void RunRestore()
    {
        if (!_rigUndo.Undo() || !SameTransform(_node.LocalTransform, _restLocal))
        {
            Fail("restore", "the final undo did not put the node back at rest");
            return;
        }

        // The UndoCount checks assume each run starts from an empty history.
        _rigUndo.Clear();
        _stage = Stage.Idle;
        _nextRunTime = _elapsed + IntervalSeconds;

        // ASCII only: a smoke gate reads this from a redirected console.
        _logger.LogInformation(
            "Editing self-test: PASS - screen-ray picked '{Node}', grabbed its x handle ({GizmoVertices} " +
            "gizmo line vertices), dragged over {Steps} frames to ({Dx:0.0000}, {Dy:0.0000}, {Dz:0.0000}) " +
            "against an expected ({Ex:0.0000}, {Ey:0.0000}, {Ez:0.0000}), error {Error:0.000000} units; " +
            "the commit recompiled {Dirty} dirty cell(s) including ({CellX}, {CellY}, {CellZ}) after " +
            "{Frames} frame(s); undo restored and redo re-applied the transform exactly; node left at rest",
            _node.Name, _gizmoVertices, DragSteps.Length,
            _measuredDelta.X, _measuredDelta.Y, _measuredDelta.Z,
            DragDelta.X, DragDelta.Y, DragDelta.Z,
            Vector3.Distance(_measuredDelta, DragDelta),
            _observedDirtyCells, _expectedCell.X, _expectedCell.Y, _expectedCell.Z,
            _compileWatchFrames);
    }

    // Always returns false, so callers can `return Fail(...)`.
    private bool Fail(string stage, string reason)
    {
        RestoreNodeAndRig();

        _logger.LogError(
            "Editing self-test: FAIL at {Stage} - {Reason}. Node '{Node}', measured delta " +
            "({Dx:0.0000}, {Dy:0.0000}, {Dz:0.0000}) against expected ({Ex:0.0000}, {Ey:0.0000}, {Ez:0.0000})",
            stage, reason, _node.Name,
            _measuredDelta.X, _measuredDelta.Y, _measuredDelta.Z,
            DragDelta.X, DragDelta.Y, DragDelta.Z);

        return false;
    }

    // A person took the pointer. Not a failure.
    private void Abandon(string reason)
    {
        // RestoreNodeAndRig resets _stage to Idle.
        Stage stage = _stage;
        RestoreNodeAndRig();
        _logger.LogDebug("Editing self-test: run abandoned at {Stage} because {Reason}", stage, reason);
    }

    private void RestoreNodeAndRig()
    {
        // Reset before Clear: Clear refuses to run with a transaction open.
        _rigGizmos.Reset();
        _rigViewport.Reset();

        _node.LocalTransform = _restLocal;
        _rigUndo.Clear();

        _stage = Stage.Idle;
        _nextRunTime = _elapsed + IntervalSeconds;
    }

    // Inverse of Camera.ScreenPointToRay's pixel mapping.
    private Vector2 WorldToScreen(Vector3 world, Vector2 viewportSize)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), _scene.Camera.GetViewProjection());
        return new Vector2(
            (clip.X / clip.W + 1f) * 0.5f * viewportSize.X,
            (1f - clip.Y / clip.W) * 0.5f * viewportSize.Y);
    }

    private static EditorInputFrame Frame(
        Vector2 cursor,
        Vector2 viewportSize,
        PointerButtons down = PointerButtons.None,
        PointerButtons pressed = PointerButtons.None,
        PointerButtons released = PointerButtons.None) =>
        new(cursor, viewportSize, down, pressed, released,
            KeyModifiers.None, Vector2.Zero, SyntheticFrameSeconds);

    private static bool SameTransform(in Transform a, in Transform b) =>
        a.Position == b.Position && a.Rotation == b.Rotation && a.Scale == b.Scale;

    private static bool Contains(IReadOnlyList<ChunkCoord> cells, ChunkCoord cell)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i] == cell)
                return true;
        }
        return false;
    }

    private void SaveSelection()
    {
        _savedSelection.Clear();
        IReadOnlyList<SceneNode> items = _scene.Selection.Items;
        for (int i = 0; i < items.Count; i++)
            _savedSelection.Add(items[i]);
    }

    private void RestoreSelection()
    {
        _scene.Selection.SetRange(_savedSelection);
        _savedSelection.Clear();
    }

    private enum Stage
    {
        Idle,
        WatchCompile,
        Undo,
        Redo,
        Restore,
    }
}
