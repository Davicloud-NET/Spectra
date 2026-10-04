using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// A view and projection source for a <see cref="Scene"/>, oriented by yaw
/// and pitch. Matrices are cached and rebuilt only when an input changes.
/// </summary>
public sealed class Camera
{
    // Just short of vertical: at ±90° the cross with world up is zero and the
    // basis goes NaN.
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

    // Own dirty flag: the View and Projection getters clear theirs on read.
    // Render thread only, so nothing here is synchronized.
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
            // Pipelines write this every frame.
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

    /// <summary>Whether this camera projects perspective or orthographic.</summary>
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
    /// How many world units the viewport's height spans, orthographic only.
    /// A value that is not finite and positive is ignored.
    /// </summary>
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
    /// cannot express. The yaw decides which way is up on screen.
    /// </summary>
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

    private const float PitchLimitVertical = MathF.PI * 0.5f;

    /// <summary>
    /// The orthonormal basis for a yaw and a pitch, including the vertical case.
    /// </summary>
    public static void BasisFor(float yaw, float pitch, out Vector3 forward, out Vector3 right, out Vector3 up)
    {
        float cosPitch = MathF.Cos(pitch);
        forward = Vector3.Normalize(new Vector3(
            MathF.Cos(yaw) * cosPitch,
            MathF.Sin(pitch),
            MathF.Sin(yaw) * cosPitch));

        // At the poles the world-up cross is zero, so build the frame from yaw.
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
                // Ortho slab is symmetric about the eye: the eye sits at the
                // focus, so geometry behind it has to render too. Depth is
                // linear here, so the wide range costs no precision.
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
            return;

        var direction = Vector3.Normalize(offset);
        _yaw = MathF.Atan2(direction.Z, direction.X);
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
    /// The world to clip matrix, row-vector convention:
    /// <c>clip = world · View · Projection</c>. Cached.
    /// </summary>
    public Matrix4x4 GetViewProjection()
    {
        EnsureViewProjection();
        return _viewProjection;
    }

    /// <summary>The camera's view frustum in world space.</summary>
    public Frustum GetFrustum() => Frustum.FromViewProjection(GetViewProjection());

    /// <summary>
    /// Builds the world-space picking ray through a screen point.
    /// <paramref name="screenPos"/> is in pixels, origin top-left, y down. The
    /// ray starts on the near plane.
    /// </summary>
    public Ray3 ScreenPointToRay(Vector2 screenPos, Vector2 viewportSize)
    {
        float ndcX = 2f * screenPos.X / viewportSize.X - 1f;
        float ndcY = 1f - 2f * screenPos.Y / viewportSize.Y;

        EnsureViewProjection();

        // Clip depth is [0, 1]: near at z = 0, far at 1.
        Vector3 nearWorld = UnprojectNdc(ndcX, ndcY, 0f);
        Vector3 farWorld = UnprojectNdc(ndcX, ndcY, 1f);

        // far - near, not near - Position: the long baseline divides away the
        // float error in the unprojected points.
        return new Ray3(nearWorld, Vector3.Normalize(farWorld - nearWorld));
    }

    /// <summary>
    /// Builds the sub-frustum bounded by a screen-space rectangle, as swept by
    /// a box selection. Corners use the pixel convention of
    /// <see cref="ScreenPointToRay"/> and may come in either order. A rectangle
    /// thinner than one pixel is widened to one.
    /// </summary>
    public Frustum ScreenRectToFrustum(Vector2 cornerA, Vector2 cornerB, Vector2 viewportSize)
    {
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return GetFrustum();

        Vector2 min = Vector2.Min(cornerA, cornerB);
        Vector2 max = Vector2.Max(cornerA, cornerB);

        // One pixel minimum, or the side planes get zero normals.
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

        // The y flip swaps which pixel edge is the NDC minimum.
        float ndcMinX = 2f * min.X / viewportSize.X - 1f;
        float ndcMaxX = 2f * max.X / viewportSize.X - 1f;
        float ndcMaxY = 1f - 2f * min.Y / viewportSize.Y;
        float ndcMinY = 1f - 2f * max.Y / viewportSize.Y;

        float centerNdcX = 0.5f * (ndcMinX + ndcMaxX);
        float centerNdcY = 0.5f * (ndcMinY + ndcMaxY);
        float halfNdcX = 0.5f * (ndcMaxX - ndcMinX);
        float halfNdcY = 0.5f * (ndcMaxY - ndcMinY);

        // Stretch the rectangle's NDC box out to [-1, 1]² and reuse the normal
        // plane extraction. z and w are untouched, so near and far survive.
        // Pre-divide: x' = x/hx - (cx/hx)·w, y' likewise.
        var remap = Matrix4x4.Identity;
        remap.M11 = 1f / halfNdcX;
        remap.M22 = 1f / halfNdcY;
        remap.M41 = -centerNdcX / halfNdcX;
        remap.M42 = -centerNdcY / halfNdcY;

        EnsureViewProjection();
        return Frustum.FromViewProjection(_viewProjection * remap);
    }

    private Vector3 UnprojectNdc(float x, float y, float z)
    {
        Vector4 h = Vector4.Transform(new Vector4(x, y, z, 1f), _inverseViewProjection);
        return new Vector3(h.X, h.Y, h.Z) / h.W;
    }

    private void EnsureViewProjection()
    {
        if (!_viewProjectionDirty) return;

        _viewProjection = View * Projection;
        // Only fails on degenerate input such as a zero fov.
        if (!Matrix4x4.Invert(_viewProjection, out _inverseViewProjection))
            _inverseViewProjection = Matrix4x4.Identity;
        _viewProjectionDirty = false;
    }
}
