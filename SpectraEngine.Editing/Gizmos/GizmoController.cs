using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Input;
using SpectraEngine.Editing.Undo;
using System;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// Owns the move, rotate and resize tools and keeps one of them live. Changing
/// mode, orientation or style cancels any drag in progress. Render thread only.
/// </summary>
public sealed class GizmoController
{
    private GizmoMode _mode = GizmoMode.Translate;
    private GizmoOrientation _orientation = GizmoOrientation.World;
    private GizmoStyle _style = GizmoStyle.Studio;

    /// <summary>
    /// Creates a manipulator over a scene and the history its edits land in.
    /// </summary>
    public GizmoController(Scene scene, UndoStack undo)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);

        Scene = scene;
        Undo = undo;
        Translate = new TranslateGizmo(scene, undo);
        Rotate = new RotateGizmo(scene, undo);
        Scale = new ScaleGizmo(scene, undo);

        Translate.Style = _style;
        Rotate.Style = _style;
        Scale.Style = _style;
    }

    /// <summary>The scene the tools manipulate.</summary>
    public Scene Scene { get; }

    /// <summary>The history every tool's drags land in.</summary>
    public UndoStack Undo { get; }

    /// <summary>The move tool.</summary>
    public TranslateGizmo Translate { get; }

    /// <summary>The rotate tool.</summary>
    public RotateGizmo Rotate { get; }

    /// <summary>The resize tool.</summary>
    public ScaleGizmo Scale { get; }

    /// <summary>
    /// Which tool is live. Assigning a different mode cancels the outgoing
    /// tool's drag.
    /// </summary>
    public GizmoMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;

            // Reset first: the outgoing tool owns the open transaction, and
            // transactions do not nest.
            Active.Reset();
            _mode = value;
            ModeChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Whether the handles lie along the world axes or the reference node's own.
    /// Applies to <see cref="Translate"/> and <see cref="Rotate"/>;
    /// <see cref="Scale"/> is local-only. Changing it cancels a live drag.
    /// </summary>
    public GizmoOrientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value)
                return;

            Active.Reset();
            _orientation = value;
            Translate.Orientation = value;
            Rotate.Orientation = value;
            OrientationChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// The manipulator style all three tools use. Defaults to
    /// <see cref="GizmoStyle.Studio"/>. Changing it cancels a live drag.
    /// </summary>
    public GizmoStyle Style
    {
        get => _style;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_style, value))
                return;

            Active.Reset();
            _style = value;
            Translate.Style = value;
            Rotate.Style = value;
            Scale.Style = value;
            StyleChanged?.Invoke(value);
        }
    }

    /// <summary>The live tool.</summary>
    public GizmoTool Active => _mode switch
    {
        GizmoMode.Rotate => Rotate,
        GizmoMode.Scale => Scale,
        _ => Translate,
    };

    /// <summary>Raised after <see cref="Mode"/> changed.</summary>
    public event Action<GizmoMode>? ModeChanged;

    /// <summary>Raised after <see cref="Orientation"/> changed.</summary>
    public event Action<GizmoOrientation>? OrientationChanged;

    /// <summary>Raised after <see cref="Style"/> changed.</summary>
    public event Action<GizmoStyle>? StyleChanged;

    /// <summary>
    /// Whether snapping is on. Reads the live tool's setting; writes all three.
    /// </summary>
    public bool SnapEnabled
    {
        get => _mode switch
        {
            GizmoMode.Rotate => Rotate.Snap.Enabled,
            GizmoMode.Scale => Scale.Snap.Enabled,
            _ => Translate.Snap.Enabled,
        };
        set
        {
            Translate.Snap.Enabled = value;
            Rotate.Snap.Enabled = value;
            Scale.Snap.Enabled = value;
        }
    }

    /// <summary>Advances the live tool by one frame. See <see cref="GizmoTool.Update"/>.</summary>
    public GizmoUpdateResult Update(in EditorInputFrame frame, bool cancelRequested = false, bool pointerAvailable = true) =>
        Active.Update(in frame, cancelRequested, pointerAvailable);

    /// <summary>Draws the live tool. See <see cref="GizmoTool.Draw"/>.</summary>
    public void Draw(DebugDraw output) => Active.Draw(output);

    /// <summary>
    /// Applies one editor verb. Returns whether it changed anything, so a host
    /// can fall through to another binding.
    /// </summary>
    public bool Apply(GizmoCommand command)
    {
        switch (command)
        {
            case GizmoCommand.UseTranslate:
                return SetMode(GizmoMode.Translate);

            case GizmoCommand.UseRotate:
                return SetMode(GizmoMode.Rotate);

            case GizmoCommand.UseScale:
                return SetMode(GizmoMode.Scale);

            case GizmoCommand.CycleMode:
                return SetMode(_mode switch
                {
                    GizmoMode.Translate => GizmoMode.Rotate,
                    GizmoMode.Rotate => GizmoMode.Scale,
                    _ => GizmoMode.Translate,
                });

            case GizmoCommand.ToggleOrientation:
                Orientation = _orientation == GizmoOrientation.World
                    ? GizmoOrientation.Local
                    : GizmoOrientation.World;
                return true;

            case GizmoCommand.ToggleStyle:
                Style = ReferenceEquals(_style, GizmoStyle.Studio)
                    ? GizmoStyle.Classic
                    : GizmoStyle.Studio;
                return true;

            case GizmoCommand.ToggleSnap:
                SnapEnabled = !SnapEnabled;
                return true;

            case GizmoCommand.UseWorldOrientation:
                return SetOrientation(GizmoOrientation.World);

            case GizmoCommand.UseLocalOrientation:
                return SetOrientation(GizmoOrientation.Local);

            case GizmoCommand.UseStudioStyle:
                return SetStyle(GizmoStyle.Studio);

            case GizmoCommand.UseClassicStyle:
                return SetStyle(GizmoStyle.Classic);

            case GizmoCommand.EnableSnap:
                return SetSnapEnabled(true);

            case GizmoCommand.DisableSnap:
                return SetSnapEnabled(false);

            case GizmoCommand.FinerSnap:
                CycleSnap(-1);
                return true;

            case GizmoCommand.CoarserSnap:
                CycleSnap(1);
                return true;

            case GizmoCommand.Cancel:
                return Active.CancelDrag();

            default:
                return false;
        }
    }

    /// <summary>
    /// Resets every tool, cancelling any drag. For a host that lost focus or
    /// replaced the scene.
    /// </summary>
    public void Reset()
    {
        Translate.Reset();
        Rotate.Reset();
        Scale.Reset();
    }

    private bool SetMode(GizmoMode mode)
    {
        if (_mode == mode)
            return false;

        Mode = mode;
        return true;
    }

    private bool SetOrientation(GizmoOrientation orientation)
    {
        if (_orientation == orientation)
            return false;

        Orientation = orientation;
        return true;
    }

    private bool SetStyle(GizmoStyle style)
    {
        if (ReferenceEquals(_style, style))
            return false;

        Style = style;
        return true;
    }

    private bool SetSnapEnabled(bool enabled)
    {
        if (SnapEnabled == enabled)
            return false;

        SnapEnabled = enabled;
        return true;
    }

    /// <summary>
    /// Sets one tool's snap increment. The value must be positive.
    /// </summary>
    public void SetSnapIncrement(GizmoMode tool, float increment)
    {
        SnapSettings snap = tool switch
        {
            GizmoMode.Rotate => Rotate.Snap,
            GizmoMode.Scale => Scale.Snap,
            _ => Translate.Snap,
        };
        snap.Increment = increment;
    }

    private void CycleSnap(int direction)
    {
        Translate.Snap.CyclePreset(direction);
        Rotate.Snap.CyclePreset(direction);
        Scale.Snap.CyclePreset(direction);
    }
}
