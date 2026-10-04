using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Input;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Cameras;

/// <summary>
/// The editor viewport's camera navigation, after Roblox Studio: right-drag
/// looks around in place with the cursor locked and the movement keys fly,
/// middle-drag pans, the wheel zooms toward the cursor, Alt-drag orbits.
/// Drives the scene's own <see cref="Camera"/>. Render thread only.
/// </summary>
// State is Position and Focus with Focus = Position + Forward * Distance.
// Each gesture holds one half and derives the other: freelook and fly hold
// Position, orbit holds Focus. Rotating about the focus and adding back would
// round the position every frame, and at x ~ 1e6 the camera visibly crawls.
//
// Gestures write target state; the live pose chases it with
// alpha = 1 - e^(-dt/tau). Distance is damped in log space because zoom is
// multiplicative.
public sealed class EditorCameraController
{
    /// <summary>Default <see cref="SmoothingTimeConstant"/>, in seconds.</summary>
    public const float DefaultSmoothingTimeConstant = 0.045f;

    /// <summary>The orbit distance a freshly adopted camera starts at, in world units.</summary>
    public const float DefaultDistance = 10f;

    /// <summary>The fly speed a fresh controller starts at, in world units per second.</summary>
    public const float DefaultFlySpeed = 12f;

    // Same limit as Camera.Pitch's clamp. A camera chasing an unclamped target
    // would stick and then jump.
    private const float PitchLimit = MathF.PI / 2f - 0.01f;

    // Cursor ray nearly perpendicular to the view axis: zoom falls back to the centre.
    private const float ParallelEpsilon = 1e-4f;

    private readonly List<SceneNode> _framingScratch = [];

    private Vector2 _lastCursor;
    private bool _wasCursorLocked;
    private bool _cursorLockRequested;
    private EditorNavigationGesture _gesture;
    private EditorNavigationGesture _lastGesture;
    private float _flySpeed = DefaultFlySpeed;

    /// <summary>
    /// Creates a controller over a scene, adopting its camera as it stands with
    /// the focus <see cref="DefaultDistance"/> units ahead.
    /// </summary>
    public EditorCameraController(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Scene = scene;
        AdoptCamera(DefaultDistance);
    }

    /// <summary>The scene whose camera this drives and whose selection it frames.</summary>
    public Scene Scene { get; }

    /// <summary>The scene's camera.</summary>
    public Camera Camera => Scene.Camera;

    /// <summary>
    /// Where cursor-lock requests go when a freelook begins and ends. Null runs
    /// without a real lock.
    /// </summary>
    public ICursorLock? CursorLock { get; set; }

    /// <summary>The camera's current position in world space.</summary>
    public Vector3 Position { get; private set; }

    /// <summary>The point the camera currently orbits, in world space.</summary>
    public Vector3 Focus { get; private set; }

    /// <summary>The camera's current distance from <see cref="Focus"/>, in world units.</summary>
    public float Distance { get; private set; } = DefaultDistance;

    /// <summary>The camera's current yaw, in radians.</summary>
    public float Yaw { get; private set; }

    /// <summary>The camera's current pitch, in radians, clamped short of vertical.</summary>
    public float Pitch { get; private set; }

    /// <summary>Where the camera is heading.</summary>
    public Vector3 TargetPosition { get; private set; }

    /// <summary>Where the focus is heading.</summary>
    public Vector3 TargetFocus { get; private set; }

    /// <summary>The distance the dolly is heading for, in world units.</summary>
    public float TargetDistance { get; private set; } = DefaultDistance;

    /// <summary>The yaw the look is heading for, in radians.</summary>
    public float TargetYaw { get; private set; }

    /// <summary>The pitch the look is heading for, in radians.</summary>
    public float TargetPitch { get; private set; }

    /// <summary>True while the live pose has not caught up with the target.</summary>
    // Focus is left out: it is derived from the damped pose each frame.
    public bool IsSettling =>
        Position != TargetPosition || Distance != TargetDistance ||
        Yaw != TargetYaw || Pitch != TargetPitch;

    /// <summary>
    /// The damping time constant in seconds, for the dolly and the glide after
    /// a gesture ends. Zero disables all damping.
    /// </summary>
    public float SmoothingTimeConstant { get; set; } = DefaultSmoothingTimeConstant;

    /// <summary>
    /// The damping time constant for look and move while a pointer gesture
    /// drives them, in seconds. Capped at <see cref="SmoothingTimeConstant"/>.
    /// </summary>
    // Zero by default: a look that trails the hand feels heavy. The wheel keeps
    // its own constant because notches arrive in steps.
    public float PointerSmoothingTimeConstant { get; set; }

    /// <summary>Radians of freelook per pixel of mouse motion.</summary>
    public float LookSensitivity { get; set; } = 0.0035f;

    /// <summary>Radians of orbit per pixel of drag.</summary>
    public float OrbitSensitivity { get; set; } = 0.006f;

    /// <summary>
    /// How fast the movement keys fly the camera, in world units per second.
    /// The wheel trims it while looking, and the value sticks for the session.
    /// </summary>
    public float FlySpeed
    {
        get => _flySpeed;
        set => _flySpeed = Math.Clamp(value, MinFlySpeed, MaxFlySpeed);
    }

    /// <summary>The slowest <see cref="FlySpeed"/> the wheel may trim down to.</summary>
    public float MinFlySpeed { get; set; } = 0.05f;

    /// <summary>The fastest <see cref="FlySpeed"/> the wheel may trim up to.</summary>
    public float MaxFlySpeed { get; set; } = 5000f;

    /// <summary>
    /// The factor one wheel notch applies to <see cref="FlySpeed"/> while the
    /// look button is held.
    /// </summary>
    public float FlySpeedStep { get; set; } = 1.2f;

    /// <summary>How much the boost modifier multiplies <see cref="FlySpeed"/> by.</summary>
    public float BoostMultiplier { get; set; } = 3f;

    /// <summary>The factor one wheel notch applies to the orbit distance.</summary>
    public float ZoomStep { get; set; } = 1.15f;

    /// <summary>The closest the camera may orbit, in world units.</summary>
    public float MinDistance { get; set; } = 0.05f;

    /// <summary>The furthest the camera may orbit, in world units.</summary>
    public float MaxDistance { get; set; } = 1e7f;

    /// <summary>
    /// Whether the wheel dollies toward the point under the cursor or straight
    /// down the view axis.
    /// </summary>
    public bool ZoomToCursor { get; set; } = true;

    /// <summary>
    /// How much slack <see cref="FrameBounds"/> leaves around what it frames.
    /// Below 1 crops it.
    /// </summary>
    public float FrameMargin { get; set; } = 1.15f;

    /// <summary>The button that starts a freelook and locks the cursor while held.</summary>
    public PointerButtons FreeLookButton { get; set; } = PointerButtons.Right;

    /// <summary>The button that orbits when <see cref="OrbitModifier"/> is held.</summary>
    public PointerButtons OrbitButton { get; set; } = PointerButtons.Right;

    /// <summary>The modifier that turns <see cref="OrbitButton"/> into an orbit.</summary>
    public KeyModifiers OrbitModifier { get; set; } = KeyModifiers.Alt;

    /// <summary>The button that orbits when <see cref="AlternateOrbitModifier"/> is held.</summary>
    public PointerButtons AlternateOrbitButton { get; set; } = PointerButtons.Left;

    /// <summary>The modifier that turns <see cref="AlternateOrbitButton"/> into an orbit.</summary>
    public KeyModifiers AlternateOrbitModifier { get; set; } = KeyModifiers.Alt;

    /// <summary>The button that pans.</summary>
    public PointerButtons PanButton { get; set; } = PointerButtons.Middle;

    /// <summary>Which navigation gesture owns the pointer right now.</summary>
    public EditorNavigationGesture ActiveGesture => _gesture;

    /// <summary>True while a navigation gesture is in progress.</summary>
    public bool IsNavigating => _gesture != EditorNavigationGesture.None;

    /// <summary>True while a freelook is in progress.</summary>
    public bool IsFreeLooking => _gesture == EditorNavigationGesture.FreeLook;

    /// <summary>True while an orbit drag is in progress.</summary>
    public bool IsOrbiting => _gesture == EditorNavigationGesture.Orbit;

    /// <summary>True while a pan drag is in progress.</summary>
    public bool IsPanning => _gesture == EditorNavigationGesture.Pan;

    /// <summary>
    /// True while this controller is asking for a locked cursor. Whether the
    /// request has landed is <see cref="Core.Input.ICursorLock.IsCursorLocked"/>.
    /// </summary>
    public bool IsCursorLockRequested => _cursorLockRequested;

    /// <summary>
    /// Consumes one frame of input, damps the live pose toward its target and
    /// writes the camera. Returns true when the camera moved. Do not call
    /// while a gizmo is dragging; call <see cref="SuspendNavigation"/> instead.
    /// </summary>
    public bool Update(in EditorInputFrame frame)
    {
        Vector2 delta = MeasureMotion(in frame);

        _lastGesture = _gesture;
        _gesture = ClassifyGesture(in frame);

        // Stays set after release, so what is left of a flick settles at the
        // pointer constant instead of gliding on the longer one.
        if (_gesture != EditorNavigationGesture.None)
            _pointerSettle = true;

        // The lock lands a frame or two after the request. Until then the
        // gesture runs off the absolute cursor.
        SetCursorLock(_gesture == EditorNavigationGesture.FreeLook);

        // Skip the frame a gesture starts or changes kind on: the cursor may
        // have travelled far since it was last tracked, and that would snap
        // the view.
        if (_gesture == _lastGesture)
        {
            switch (_gesture)
            {
                case EditorNavigationGesture.FreeLook:
                    ApplyFreeLook(delta);
                    break;
                case EditorNavigationGesture.Orbit:
                    ApplyOrbit(delta);
                    break;
                case EditorNavigationGesture.Pan:
                    ApplyPan(delta, frame.ViewportSize);
                    break;
            }
        }

        if (frame.ScrollDelta.Y != 0f)
        {
            // Tested on the button, not the gesture, so a notch on the press
            // frame or during an Alt orbit still trims speed.
            if (frame.IsDown(FreeLookButton))
            {
                AdjustFlySpeed(frame.ScrollDelta.Y);
            }
            else
            {
                // While the lock is still on, the cursor position is stale:
                // zoom down the view axis.
                Vector2 target = frame.IsCursorLocked ? frame.ViewportSize * 0.5f : frame.CursorPosition;
                ApplyZoom(frame.ScrollDelta.Y, target, frame.ViewportSize);

                _pointerSettle = false;
            }
        }

        if (!frame.Navigation.IsIdle)
            ApplyMove(frame.Navigation, frame.DeltaTime);

        return Settle(frame.DeltaTime);
    }

    /// <summary>
    /// Whether the press arriving on this frame belongs to navigation, so the
    /// viewport must not treat it as a pick, a marquee or a handle grab.
    /// </summary>
    public bool ClaimsPress(in EditorInputFrame frame) =>
        frame.WasPressed(FreeLookButton) ||
        frame.WasPressed(PanButton) ||
        (frame.WasPressed(OrbitButton) && frame.HasModifiers(OrbitModifier)) ||
        (frame.WasPressed(AlternateOrbitButton) && frame.HasModifiers(AlternateOrbitModifier));

    /// <summary>
    /// Whether navigation owns this frame's pointer: the press belongs to it
    /// (<see cref="ClaimsPress"/>) or an earlier gesture is still held.
    /// </summary>
    // ClaimsPress only sees press edges, so a left click during a held pan
    // would otherwise be read as a pick. The hold is tested against this
    // frame's buttons so the release frame hands the pointer back.
    public bool OwnsPointer(in EditorInputFrame frame) =>
        ClaimsPress(in frame) ||
        (IsNavigating && ClassifyGesture(in frame) != EditorNavigationGesture.None);

    /// <summary>
    /// Drops any gesture in progress and releases the cursor. Call on every
    /// frame the host skips <see cref="Update"/>.
    /// </summary>
    // The drag delta is measured against the last cursor Update saw. Without
    // this, the travel from the skipped frames lands as one step on resume.
    public void SuspendNavigation()
    {
        _gesture = EditorNavigationGesture.None;
        _lastGesture = EditorNavigationGesture.None;
        SetCursorLock(false);
    }

    /// <summary>
    /// Turns the camera in place by a mouse motion in pixels. The position does
    /// not change. Leaves an orthographic view.
    /// </summary>
    public void ApplyFreeLook(Vector2 pixelDelta)
    {
        if (View != EditorViewPreset.Perspective) SetView(EditorViewPreset.Perspective);

        TargetYaw += pixelDelta.X * LookSensitivity;
        TargetPitch = Math.Clamp(TargetPitch - pixelDelta.Y * LookSensitivity, -PitchLimit, PitchLimit);
        NormalizeYaw();

        // Position is held; the focus is derived.
        TargetFocus = TargetPosition + TargetForward() * TargetDistance;
    }

    /// <summary>
    /// Swings the camera around <see cref="TargetFocus"/> at a fixed distance.
    /// Leaves an orthographic view.
    /// </summary>
    public void ApplyOrbit(Vector2 pixelDelta)
    {
        if (View != EditorViewPreset.Perspective) SetView(EditorViewPreset.Perspective);

        TargetYaw += pixelDelta.X * OrbitSensitivity;
        TargetPitch = Math.Clamp(TargetPitch - pixelDelta.Y * OrbitSensitivity, -PitchLimit, PitchLimit);
        NormalizeYaw();

        // Focus is held; the position is derived.
        TargetPosition = TargetFocus - TargetForward() * TargetDistance;
    }

    /// <summary>
    /// Flies the camera along <paramref name="navigation"/>'s axis for
    /// <paramref name="deltaTime"/> seconds. X and Z follow the camera's right
    /// and forward, Y is world up. The axis is normalized.
    /// </summary>
    public void ApplyMove(in EditorNavigationInput navigation, float deltaTime)
    {
        if (navigation.IsIdle || deltaTime <= 0f)
            return;

        Basis(TargetYaw, TargetPitch, out Vector3 forward, out Vector3 right);
        Vector3 direction =
            right * navigation.Move.X +
            Vector3.UnitY * navigation.Move.Y +
            forward * navigation.Move.Z;

        float length = direction.Length();
        if (length <= 0f)
            return;

        float speed = navigation.Boost ? FlySpeed * MathF.Max(BoostMultiplier, 0f) : FlySpeed;
        Vector3 step = direction / length * (speed * deltaTime);

        TargetPosition += step;
        TargetFocus += step;
    }

    /// <summary>
    /// Trims <see cref="FlySpeed"/> by <paramref name="notches"/> wheel steps
    /// (positive is faster), multiplicatively and clamped.
    /// </summary>
    public void AdjustFlySpeed(float notches)
    {
        float step = MathF.Max(FlySpeedStep, 1.0001f);
        FlySpeed = _flySpeed * MathF.Pow(step, notches);
    }

    /// <summary>
    /// Slides the camera in its view plane so the world point under the cursor
    /// at the focus depth follows the cursor.
    /// </summary>
    public void ApplyPan(Vector2 pixelDelta, Vector2 viewportSize)
    {
        if (viewportSize.Y <= 0f)
            return;

        float worldPerPixel = GizmoMath.WorldPerPixel(Camera, viewportSize.Y, TargetDistance);

        // The camera moves against the drag. Screen y grows down.
        Vector3 step =
            Camera.Right * (-pixelDelta.X * worldPerPixel) +
            Camera.Up * (pixelDelta.Y * worldPerPixel);

        TargetFocus += step;
        TargetPosition += step;
    }

    /// <summary>
    /// Dollies by <paramref name="notches"/> wheel steps (positive is toward
    /// the scene). With <see cref="ZoomToCursor"/> on, the world point under
    /// <paramref name="cursorPosition"/> keeps projecting to that pixel.
    /// </summary>
    // With P the cursor ray's hit on the focus plane and s the distance scale,
    // moving both the focus and the eye the fraction (1 - s) toward P keeps P's
    // offset-to-depth ratio, so it stays on the same pixel.
    public void ApplyZoom(float notches, Vector2 cursorPosition, Vector2 viewportSize)
    {
        float scale = MathF.Pow(ZoomStep, -notches);
        float newDistance = Math.Clamp(TargetDistance * scale, MinDistance, MaxDistance);
        // Ratio from the clamped distance, or at the stops the eye keeps
        // creeping toward the cursor.
        float applied = TargetDistance > 0f ? newDistance / TargetDistance : 1f;
        TargetDistance = newDistance;

        if (applied == 1f)
            return;

        if (ZoomToCursor && viewportSize.X > 0f && viewportSize.Y > 0f &&
            TryFocusPlanePoint(cursorPosition, viewportSize, out Vector3 point))
        {
            TargetFocus += (point - TargetFocus) * (1f - applied);
            TargetPosition += (point - TargetPosition) * (1f - applied);
            return;
        }

        TargetPosition = TargetFocus - TargetForward() * TargetDistance;
    }

    /// <summary>
    /// Frames the current selection with <see cref="FrameMargin"/> to spare.
    /// Returns false when the selection is empty.
    /// </summary>
    public bool FrameSelection()
    {
        IReadOnlyList<SceneNode> items = Scene.Selection.Items;
        if (items.Count == 0)
            return false;

        if (!TryUnionBounds(items, out Aabb bounds))
            return false;

        FrameBounds(bounds);
        return true;
    }

    /// <summary>
    /// Frames every spatial node in the scene. Returns false for a scene with
    /// no spatial nodes.
    /// </summary>
    public bool FrameAll()
    {
        _framingScratch.Clear();
        foreach (SceneNode node in Scene.Nodes)
        {
            if (Scene.TryGetWorldBounds(node, out _))
                _framingScratch.Add(node);
        }

        if (!TryUnionBounds(_framingScratch, out Aabb bounds))
            return false;

        FrameBounds(bounds);
        _framingScratch.Clear();
        return true;
    }

    /// <summary>
    /// Aims the camera at <paramref name="bounds"/>: the focus goes to its
    /// centre and the distance fits its bounding sphere in view, times
    /// <see cref="FrameMargin"/>. The clip planes are not changed.
    /// </summary>
    // The sphere, not the box, so the distance does not depend on view angle.
    public void FrameBounds(Aabb bounds)
    {
        Vector3 center = bounds.Center;
        float radius = 0.5f * bounds.Size.Length();

        // A point selection keeps the current distance.
        if (radius > 0f)
        {
            float halfVertical = Camera.FieldOfView * 0.5f;
            float halfHorizontal = MathF.Atan(MathF.Tan(halfVertical) * MathF.Max(Camera.AspectRatio, 1e-4f));
            float halfAngle = MathF.Min(halfVertical, halfHorizontal);

            // Sphere touching the view cone: d = r / sin(halfAngle).
            float distance = radius / MathF.Max(MathF.Sin(halfAngle), 1e-4f) * MathF.Max(FrameMargin, 1e-3f);
            TargetDistance = Math.Clamp(distance, MinDistance, MaxDistance);
        }

        TargetFocus = center;
        TargetPosition = TargetFocus - TargetForward() * TargetDistance;

        _pointerSettle = false;
    }

    /// <summary>Applies one navigation verb. Returns whether it changed anything.</summary>
    public bool Apply(EditorCameraCommand command) => command switch
    {
        EditorCameraCommand.FrameSelection => FrameSelection(),
        EditorCameraCommand.FrameAll => FrameAll(),

        EditorCameraCommand.ViewPerspective => SetView(EditorViewPreset.Perspective),
        EditorCameraCommand.ViewTop => SetView(EditorViewPreset.Top),
        EditorCameraCommand.ViewBottom => SetView(EditorViewPreset.Bottom),
        EditorCameraCommand.ViewFront => SetView(EditorViewPreset.Front),
        EditorCameraCommand.ViewBack => SetView(EditorViewPreset.Back),
        EditorCameraCommand.ViewRight => SetView(EditorViewPreset.Right),
        EditorCameraCommand.ViewLeft => SetView(EditorViewPreset.Left),

        _ => false,
    };

    /// <summary>Which view this camera is showing.</summary>
    public EditorViewPreset View { get; private set; } = EditorViewPreset.Perspective;

    /// <summary>
    /// Switches view, without damping. Returns whether anything changed.
    /// </summary>
    // Snaps: a damped yaw swing under an orthographic projection looks like
    // the picture shearing, not a camera turning.
    public bool SetView(EditorViewPreset preset)
    {
        if (View == preset) return false;

        View = preset;

        if (EditorViewPresets.IsOrthographic(preset))
        {
            TargetYaw = EditorViewPresets.YawOf(preset);

            // Not clamped: top and bottom need a vertical pitch.
            // Camera.SetVerticalView builds the basis there.
            TargetPitch = EditorViewPresets.PitchOf(preset);
        }
        else
        {
            TargetPitch = Math.Clamp(TargetPitch, -PitchLimit, PitchLimit);
        }

        NormalizeYaw();
        TargetPosition = TargetFocus - (TargetForward() * TargetDistance);
        SnapToTarget();
        return true;
    }

    /// <summary>
    /// Places the camera by its focus, target and live state together, with no
    /// damping.
    /// </summary>
    public void SetOrbit(Vector3 focus, float distance, float yaw, float pitch)
    {
        TargetFocus = focus;
        TargetDistance = Math.Clamp(distance, MinDistance, MaxDistance);
        TargetYaw = yaw;
        TargetPitch = Math.Clamp(pitch, -PitchLimit, PitchLimit);
        NormalizeYaw();
        TargetPosition = TargetFocus - TargetForward() * TargetDistance;
        SnapToTarget();
    }

    /// <summary>
    /// Places the camera by its position, with the focus
    /// <paramref name="distance"/> units ahead and no damping.
    /// </summary>
    public void SetPose(Vector3 position, float yaw, float pitch, float distance = DefaultDistance)
    {
        TargetDistance = Math.Clamp(distance, MinDistance, MaxDistance);
        TargetYaw = yaw;
        TargetPitch = Math.Clamp(pitch, -PitchLimit, PitchLimit);
        NormalizeYaw();
        TargetPosition = position;
        TargetFocus = TargetPosition + TargetForward() * TargetDistance;
        SnapToTarget();
    }

    /// <summary>
    /// Adopts the camera's current pose as both live and target, with the focus
    /// <paramref name="distance"/> units ahead.
    /// </summary>
    public void AdoptCamera(float distance = DefaultDistance) =>
        SetPose(Camera.Position, Camera.Yaw, Camera.Pitch, distance);

    /// <summary>Sets the live state to the target and writes the camera.</summary>
    public void SnapToTarget()
    {
        Position = TargetPosition;
        Focus = TargetFocus;
        Distance = TargetDistance;
        Yaw = TargetYaw;
        Pitch = TargetPitch;
        WriteCamera();
    }

    // Precedence: orbit (button plus modifier), then freelook, then pan.
    // Shared by Update and OwnsPointer so the two cannot disagree.
    private EditorNavigationGesture ClassifyGesture(in EditorInputFrame frame)
    {
        if ((frame.IsDown(OrbitButton) && frame.HasModifiers(OrbitModifier)) ||
            (frame.IsDown(AlternateOrbitButton) && frame.HasModifiers(AlternateOrbitModifier)))
        {
            return EditorNavigationGesture.Orbit;
        }

        if (frame.IsDown(FreeLookButton))
            return EditorNavigationGesture.FreeLook;

        return frame.IsDown(PanButton) ? EditorNavigationGesture.Pan : EditorNavigationGesture.None;
    }

    private Vector2 MeasureMotion(in EditorInputFrame frame)
    {
        // A locked cursor has no meaningful absolute position, only a delta.
        Vector2 delta = frame.IsCursorLocked ? frame.CursorDelta : frame.CursorPosition - _lastCursor;

        // The frame the lock changes on carries the backend's teleport. Drop it.
        if (frame.IsCursorLocked != _wasCursorLocked)
            delta = Vector2.Zero;

        _lastCursor = frame.CursorPosition;
        _wasCursorLocked = frame.IsCursorLocked;
        return delta;
    }

    private void SetCursorLock(bool wanted)
    {
        if (wanted == _cursorLockRequested)
            return;

        _cursorLockRequested = wanted;
        CursorLock?.RequestCursorMode(wanted ? CursorMode.Locked : CursorMode.Normal);
    }

    // True while look and move settle pointer-driven motion. Set by a gesture,
    // cleared by a wheel zoom or a framing.
    private bool _pointerSettle;

    // Damps the live state toward the target and writes the camera. Returns
    // whether anything moved. Two alphas: look and move use the pointer
    // constant while _pointerSettle, the dolly always uses the long one.
    private bool Settle(float deltaTime)
    {
        if (!IsSettling)
            return false;

        float lookMoveTau = _pointerSettle
            ? MathF.Min(PointerSmoothingTimeConstant, SmoothingTimeConstant)
            : SmoothingTimeConstant;

        float alphaMove = SmoothingAlpha(deltaTime, lookMoveTau);
        float alphaZoom = SmoothingAlpha(deltaTime, SmoothingTimeConstant);

        if (alphaMove >= 1f && alphaZoom >= 1f)
        {
            SnapToTarget();
            return true;
        }

        Position += (TargetPosition - Position) * alphaMove;
        Yaw += (TargetYaw - Yaw) * alphaMove;
        Pitch += (TargetPitch - Pitch) * alphaMove;
        // Geometric, because zoom is multiplicative.
        Distance = Distance > 0f && TargetDistance > 0f
            ? Distance * MathF.Pow(TargetDistance / Distance, alphaZoom)
            : TargetDistance;

        // An alpha of one can still leave float residue, so pin to the target.
        if (alphaMove >= 1f)
        {
            Position = TargetPosition;
            Yaw = TargetYaw;
            Pitch = TargetPitch;
        }
        if (alphaZoom >= 1f)
            Distance = TargetDistance;

        // Focus is derived from the damped pose, not blended.
        Basis(Yaw, Pitch, out Vector3 forward, out _);
        Focus = Position + forward * Distance;

        WriteCamera();
        return true;
    }

    // Frame-rate-independent exponential filter. 1 means arrive now.
    private static float SmoothingAlpha(float deltaTime, float timeConstant)
    {
        if (timeConstant <= 0f || deltaTime <= 0f)
            return 1f;

        return 1f - MathF.Exp(-deltaTime / timeConstant);
    }

    private void WriteCamera()
    {
        bool orthographic = EditorViewPresets.IsOrthographic(View);
        Camera.ProjectionKind = orthographic
            ? CameraProjectionKind.Orthographic
            : CameraProjectionKind.Perspective;

        if (View is EditorViewPreset.Top or EditorViewPreset.Bottom)
        {
            Camera.SetVerticalView(View == EditorViewPreset.Top, Yaw);
        }
        else
        {
            Camera.Yaw = Yaw;
            Camera.Pitch = Pitch;
        }

        // Orthographic: the eye sits at the focus and the clip slab is
        // symmetric about it. A pulled-back eye would clip everything between
        // it and the focus.
        Camera.Position = orthographic ? Focus : Position;

        if (orthographic)
        {
            // What a perspective camera spans at this distance, so switching
            // projection shows the same amount of world.
            Camera.OrthographicHeight =
                2f * Distance * MathF.Tan(Camera.FieldOfView * 0.5f);
        }
    }

    // Gestures work in target space. Camera.Forward is the damped pose.
    private Vector3 TargetForward()
    {
        Basis(TargetYaw, TargetPitch, out Vector3 forward, out _);
        return forward;
    }

    // Camera's own construction: it handles the vertical poles.
    private static void Basis(float yaw, float pitch, out Vector3 forward, out Vector3 right)
    {
        Camera.BasisFor(yaw, pitch, out forward, out right, out _);
    }

    // Shifts target and live yaw by the same whole turns, so the gap the
    // damping is closing does not change.
    private void NormalizeYaw()
    {
        const float Turn = MathF.Tau;
        float shift = MathF.Floor((TargetYaw + MathF.PI) / Turn) * Turn;
        if (shift == 0f)
            return;

        TargetYaw -= shift;
        Yaw -= shift;
    }

    // Where the cursor ray crosses the plane through the target focus. Uses the
    // live camera's ray, which is approximate while the camera is settling.
    private bool TryFocusPlanePoint(Vector2 cursorPosition, Vector2 viewportSize, out Vector3 point)
    {
        Ray3 ray = Camera.ScreenPointToRay(cursorPosition, viewportSize);
        Vector3 forward = Camera.Forward;

        float denominator = Vector3.Dot(ray.Direction, forward);
        if (MathF.Abs(denominator) < ParallelEpsilon)
        {
            point = default;
            return false;
        }

        float travel = Vector3.Dot(TargetFocus - ray.Origin, forward) / denominator;
        point = ray.PointAt(travel);
        return true;
    }

    private bool TryUnionBounds(IReadOnlyList<SceneNode> nodes, out Aabb bounds)
    {
        bool any = false;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode node = nodes[i];
            if (Scene.TryGetWorldBounds(node, out Aabb box))
            {
                min = Vector3.Min(min, box.Min);
                max = Vector3.Max(max, box.Max);
            }
            else
            {
                // A group has no bounds; use its origin.
                Vector3 origin = node.WorldPosition;
                min = Vector3.Min(min, origin);
                max = Vector3.Max(max, origin);
            }
            any = true;
        }

        bounds = any ? new Aabb(min, max) : default;
        return any;
    }
}
