using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// A free-standing view/projection source for a <see cref="Scene"/>.
/// Maintains an orthonormal basis from yaw/pitch angles. The view and
/// projection matrices are cached and rebuilt only when an input changed,
/// because they are read once per renderable per frame; the combined
/// view-projection and its inverse (frustum extraction, picking rays) share
/// the same lazy scheme.
/// </summary>
public sealed class Camera
{
    // Pitch stops just short of ±90°: exactly vertical makes Forward parallel
    // to world up, the Cross in RecomputeBasis collapses to zero, and
    // normalizing that zero vector would NaN the whole basis and view matrix.
    private const float PitchLimit = MathF.PI / 2f - 0.01f;

    private Vector3 _position = new(0f, 0f, 3f);
    private float _fieldOfView = MathF.PI / 3f;
    private float _aspectRatio = 16f / 9f;
    private float _nearPlane = 0.1f;
    private float _farPlane = 1000f;
    private float _yaw = -MathF.PI / 2f;
    private float _pitch;

    private Matrix4x4 _view;
    private Matrix4x4 _projection;
    private bool _viewDirty = true;
    private bool _projectionDirty = true;

    // The combined view-projection and its inverse (for unprojection) depend on
    // BOTH matrices, and the View/Projection getters clear their own flags on
    // read — so the pair gets its own flag, set at every site that dirties
    // either input. Render thread only, like all camera state — the flags are
    // deliberately unsynchronized because scene updates are single-threaded.
    private Matrix4x4 _viewProjection;
    private Matrix4x4 _inverseViewProjection;
    private bool _viewProjectionDirty = true;

    public Vector3 Position
    {
        get => _position;
        set
        {
            if (value == _position) return;
            _position = value;
            _viewDirty = true;
            _viewProjectionDirty = true;
        }
    }

    public Vector3 Forward { get; private set; } = -Vector3.UnitZ;
    public Vector3 Up { get; private set; } = Vector3.UnitY;
    public Vector3 Right { get; private set; } = Vector3.UnitX;

    public float FieldOfView
    {
        get => _fieldOfView;
        set
        {
            if (value == _fieldOfView) return;
            _fieldOfView = value;
            _projectionDirty = true;
            _viewProjectionDirty = true;
        }
    }

    public float AspectRatio
    {
        get => _aspectRatio;
        set
        {
            // Pipelines write this every frame; the early-out keeps the cached
            // projection valid as long as the framebuffer size is stable.
            if (value == _aspectRatio) return;
            _aspectRatio = value;
            _projectionDirty = true;
            _viewProjectionDirty = true;
        }
    }

    public float NearPlane
    {
        get => _nearPlane;
        set
        {
            if (value == _nearPlane) return;
            _nearPlane = value;
            _projectionDirty = true;
            _viewProjectionDirty = true;
        }
    }

    public float FarPlane
    {
        get => _farPlane;
        set
        {
            if (value == _farPlane) return;
            _farPlane = value;
            _projectionDirty = true;
            _viewProjectionDirty = true;
        }
    }

    public float Yaw
    {
        get => _yaw;
        set
        {
            if (value == _yaw) return;
            _yaw = value;
            RecomputeBasis();
        }
    }

    public float Pitch
    {
        get => _pitch;
        set
        {
            float clamped = Math.Clamp(value, -PitchLimit, PitchLimit);
            if (clamped == _pitch) return;
            _pitch = clamped;
            RecomputeBasis();
        }
    }

    private CameraProjectionKind _projectionKind = CameraProjectionKind.Perspective;
    private float _orthographicHeight = 10f;

    /// <summary>
    /// Whether this camera projects perspective or orthographic.
    /// </summary>
    /// <remarks>
    /// <b>A plan view is a different PROJECTION, not a distant perspective one.</b>
    /// Backing a perspective camera far off and narrowing its field of view gets
    /// close and is never right: parallel walls still converge, so a wall a
    /// person is trying to align by eye is a fraction of a degree off and the
    /// number they read off the screen is not the number in the file.
    /// </remarks>
    public CameraProjectionKind ProjectionKind
    {
        get => _projectionKind;
        set
        {
            if (_projectionKind == value) return;

            _projectionKind = value;
            _projectionDirty = true;
            _viewProjectionDirty = true;
        }
    }

    /// <summary>
    /// How many world units the viewport's HEIGHT spans, orthographic only.
    /// </summary>
    /// <remarks>
    /// The zoom of a plan view, and the one number every screen-space size on
    /// this path derives from. A value at or below zero keeps the previous one
    /// rather than throwing: this is written from the render thread every frame,
    /// and a throw there ends the session over a number a controller can simply
    /// decline to apply.
    /// </remarks>
    public float OrthographicHeight
    {
        get => _orthographicHeight;
        set
        {
            if (!float.IsFinite(value) || value <= 0f || value == _orthographicHeight) return;

            _orthographicHeight = value;
            _projectionDirty = true;
            _viewProjectionDirty = true;
        }
    }

    /// <summary>
    /// Points the camera straight down or straight up, which the pitch clamp
    /// cannot express.
    /// </summary>
    /// <remarks>
    /// <b>The clamp exists because the ordinary basis collapses at the poles</b>:
    /// crossing a vertical forward with world up gives a zero right vector and
    /// every derived axis becomes NaN. A top view needs exactly that pitch, so
    /// the basis is built from the YAW instead, which is what decides which way
    /// north points on the screen.
    /// </remarks>
    public void SetVerticalView(bool lookingDown, float yaw)
    {
        _yaw = yaw;
        _pitch = lookingDown ? -PitchLimitVertical : PitchLimitVertical;

        BasisFor(_yaw, _pitch, out Vector3 forward, out Vector3 right, out Vector3 up);
        Forward = forward;
        Right = right;
        Up = up;

        _viewDirty = true;
        _viewProjectionDirty = true;
    }

    /// <summary>Exactly vertical, for the two views that need it.</summary>
    private const float PitchLimitVertical = MathF.PI * 0.5f;

    /// <summary>
    /// The orthonormal basis for a yaw and a pitch, including the vertical case.
    /// </summary>
    /// <remarks>
    /// Shared with <c>EditorCameraController</c>, which used to carry its own
    /// copy: two expressions of one basis drift exactly where nothing fails, and
    /// the way that presents is a gizmo whose handles point somewhere other than
    /// the axes the camera is showing.
    /// </remarks>
    public static void BasisFor(float yaw, float pitch, out Vector3 forward, out Vector3 right, out Vector3 up)
    {
        float cosPitch = MathF.Cos(pitch);
        forward = Vector3.Normalize(new Vector3(
            MathF.Cos(yaw) * cosPitch,
            MathF.Sin(pitch),
            MathF.Sin(yaw) * cosPitch));

        // At the poles the world-up cross collapses to zero and every axis
        // derived from it becomes NaN, so the screen frame comes from the yaw.
        if (MathF.Abs(MathF.Sin(pitch)) >= 1f - 1e-6f)
        {
            up = new Vector3(MathF.Cos(yaw), 0f, MathF.Sin(yaw));
            right = Vector3.Normalize(Vector3.Cross(forward, up));
            up = Vector3.Normalize(Vector3.Cross(right, forward));
            return;
        }

        right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        up = Vector3.Normalize(Vector3.Cross(right, forward));
    }

    public Matrix4x4 View
    {
        get
        {
            if (_viewDirty)
            {
                _view = Matrix4x4.CreateLookAt(_position, _position + Forward, Up);
                _viewDirty = false;
            }
            return _view;
        }
    }

    public Matrix4x4 Projection
    {
        get
        {
            if (_projectionDirty)
            {
                // A SLAB symmetric about the eye, rather than a box starting at
                // the near plane. The controller puts the eye at the focus under
                // orthographic, so geometry on both sides of the focus plane has
                // to render: a box from the eye forward would clip away
                // everything between the camera and what it is looking at, which
                // in a top view is the ceiling of every room. Depth is linear
                // here, so a range this wide costs nothing in precision.
                _projection = _projectionKind == CameraProjectionKind.Orthographic
                    ? Matrix4x4.CreateOrthographic(
                        _orthographicHeight * _aspectRatio, _orthographicHeight, -_farPlane, _farPlane)
                    : Matrix4x4.CreatePerspectiveFieldOfView(
                        _fieldOfView, _aspectRatio, _nearPlane, _farPlane);
                _projectionDirty = false;
            }
            return _projection;
        }
    }

    public void LookAt(Vector3 target)
    {
        var offset = target - _position;
        if (offset.LengthSquared() < 1e-12f)
            return; // no defined direction — keep the current orientation

        var direction = Vector3.Normalize(offset);
        _yaw = MathF.Atan2(direction.Z, direction.X);
        // Same clamp as the Pitch setter: a straight-up/straight-down target
        // would otherwise put the basis into the degenerate ±90° case.
        _pitch = Math.Clamp(MathF.Asin(direction.Y), -PitchLimit, PitchLimit);
        RecomputeBasis();
    }

    private void RecomputeBasis()
    {
        BasisFor(_yaw, _pitch, out Vector3 forward, out Vector3 right, out Vector3 up);
        Forward = forward;
        Right = right;
        Up = up;
        _viewDirty = true;
        _viewProjectionDirty = true;
    }

    /// <summary>
    /// The combined world→clip matrix in the engine's row-vector convention:
    /// <c>clip = world · View · Projection</c>. Cached (together with its
    /// inverse) behind the same dirty-flag scheme as <see cref="View"/> and
    /// <see cref="Projection"/>, so repeated per-frame reads are free.
    /// </summary>
    public Matrix4x4 GetViewProjection()
    {
        EnsureViewProjection();
        return _viewProjection;
    }

    /// <summary>
    /// The camera's current view frustum in world space, for visibility
    /// queries and culling. A stack-only value snapshot — cheap to build, no
    /// allocation.
    /// </summary>
    public Frustum GetFrustum() => Frustum.FromViewProjection(GetViewProjection());

    /// <summary>
    /// Builds the world-space picking ray through a screen point.
    /// Convention: <paramref name="screenPos"/> is in pixels with the origin at
    /// the TOP-LEFT of the viewport and y growing DOWNWARD — i.e. raw window
    /// mouse coordinates; <paramref name="viewportSize"/> is the viewport extent
    /// in the same units. The ray origin lies on the near plane and the
    /// normalized direction points toward the far plane through that pixel.
    /// </summary>
    public Ray3 ScreenPointToRay(Vector2 screenPos, Vector2 viewportSize)
    {
        // Pixel → NDC: x maps to [-1, 1] left→right; y is flipped because
        // screen y grows down while NDC y grows up.
        float ndcX = 2f * screenPos.X / viewportSize.X - 1f;
        float ndcY = 1f - 2f * screenPos.Y / viewportSize.Y;

        EnsureViewProjection();

        // CreatePerspectiveFieldOfView maps depth to the D3D-style [0, 1] clip
        // range, so the near plane unprojects at z = 0 and the far plane at 1.
        Vector3 nearWorld = UnprojectNdc(ndcX, ndcY, 0f);
        Vector3 farWorld = UnprojectNdc(ndcX, ndcY, 1f);

        // far − near rather than near − Position for the direction: both
        // unprojected points carry absolute float error proportional to their
        // distance, and the long near→far baseline divides that error away.
        return new Ray3(nearWorld, Vector3.Normalize(farWorld - nearWorld));
    }

    /// <summary>
    /// Builds the sub-frustum of this camera bounded by a screen-space
    /// rectangle — the volume a marquee/box selection sweeps. The two corners
    /// are given in the same pixel convention as
    /// <see cref="ScreenPointToRay"/> (top-left origin, y growing downward) and
    /// in either order; the near and far planes are the camera's own, so the
    /// result is exactly this camera's frustum with its four side planes pulled
    /// in to the rectangle.
    /// </summary>
    /// <remarks>
    /// <b>Why a matrix and not four corner rays.</b> The rectangle's side
    /// planes are derived by post-multiplying the view-projection with the
    /// affine NDC remap that stretches the rectangle back out to the full
    /// [-1, 1] clip square, then running the same Gribb–Hartmann extraction
    /// <see cref="GetFrustum"/> uses. That reuses one tested plane derivation
    /// instead of adding a second one built from cross products of corner ray
    /// directions, and it gets the near and far planes right for free.
    /// Cross-checked against <see cref="ScreenPointToRay"/> in the tests: every
    /// corner ray lies on the two side planes that meet at its corner.
    /// <para>
    /// A rectangle thinner than one pixel in either axis is widened to one
    /// pixel rather than producing degenerate (zero-normal) side planes: a
    /// click is a one-pixel rectangle, not an empty volume, so a caller that
    /// forwards a click here still gets a usable frustum. Callers that want
    /// click-versus-drag semantics decide that above this call.
    /// </para>
    /// </remarks>
    /// <param name="cornerA">One corner of the rectangle, in viewport pixels.</param>
    /// <param name="cornerB">The opposite corner, in viewport pixels.</param>
    /// <param name="viewportSize">Viewport extent in the same pixel units.</param>
    public Frustum ScreenRectToFrustum(Vector2 cornerA, Vector2 cornerB, Vector2 viewportSize)
    {
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return GetFrustum(); // Nothing has been laid out yet; the rect means nothing.

        Vector2 min = Vector2.Min(cornerA, cornerB);
        Vector2 max = Vector2.Max(cornerA, cornerB);

        // One pixel is the floor on either axis (see the remarks): half a pixel
        // out from the rectangle's own centre on the offending axis.
        if (max.X - min.X < 1f)
        {
            float centerX = 0.5f * (min.X + max.X);
            min.X = centerX - 0.5f;
            max.X = centerX + 0.5f;
        }
        if (max.Y - min.Y < 1f)
        {
            float centerY = 0.5f * (min.Y + max.Y);
            min.Y = centerY - 0.5f;
            max.Y = centerY + 0.5f;
        }

        // Pixels → NDC, with the same y flip ScreenPointToRay applies. Note the
        // flip swaps which pixel edge is the NDC minimum on y.
        float ndcMinX = 2f * min.X / viewportSize.X - 1f;
        float ndcMaxX = 2f * max.X / viewportSize.X - 1f;
        float ndcMaxY = 1f - 2f * min.Y / viewportSize.Y;
        float ndcMinY = 1f - 2f * max.Y / viewportSize.Y;

        float centerNdcX = 0.5f * (ndcMinX + ndcMaxX);
        float centerNdcY = 0.5f * (ndcMinY + ndcMaxY);
        float halfNdcX = 0.5f * (ndcMaxX - ndcMinX);
        float halfNdcY = 0.5f * (ndcMaxY - ndcMinY);

        // The remap, in the engine's row-vector convention: after the
        // perspective divide it sends the rectangle's NDC box to [-1, 1]², and
        // it leaves z and w alone so the near and far planes survive untouched.
        // Expressed pre-divide it is x' = x/hx − (cx/hx)·w, y' likewise.
        var remap = Matrix4x4.Identity;
        remap.M11 = 1f / halfNdcX;
        remap.M22 = 1f / halfNdcY;
        remap.M41 = -centerNdcX / halfNdcX;
        remap.M42 = -centerNdcY / halfNdcY;

        EnsureViewProjection();
        return Frustum.FromViewProjection(_viewProjection * remap);
    }

    /// <summary>Maps an NDC point back to world space through the cached inverse view-projection.</summary>
    private Vector3 UnprojectNdc(float x, float y, float z)
    {
        Vector4 h = Vector4.Transform(new Vector4(x, y, z, 1f), _inverseViewProjection);
        // The inverse of a perspective transform lands at w ≠ 1; dividing by w
        // restores the affine world-space point (the unproject counterpart of
        // the forward perspective divide).
        return new Vector3(h.X, h.Y, h.Z) / h.W;
    }

    private void EnsureViewProjection()
    {
        if (!_viewProjectionDirty) return;

        _viewProjection = View * Projection;
        // A perspective view-projection built from valid parameters is always
        // invertible; the guard only keeps degenerate inputs (e.g. zero fov)
        // from poisoning later unprojections with NaNs.
        if (!Matrix4x4.Invert(_viewProjection, out _inverseViewProjection))
            _inverseViewProjection = Matrix4x4.Identity;
        _viewProjectionDirty = false;
    }
}
