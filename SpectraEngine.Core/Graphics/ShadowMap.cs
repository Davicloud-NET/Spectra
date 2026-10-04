using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// A directional light's cascaded depth map, and the transforms that read it.
/// Cascades are fitted to slices of the camera frustum out to <see cref="Distance"/>.
/// </summary>
// One depth atlas in quadrants, not N textures: SpectraShade cannot pass a
// sampler to a function, so N textures would mean the filter kernel N times.
public sealed class ShadowMap : IDisposable
{
    /// <summary>Default square resolution of the whole atlas: four 1024 cascades, 16 MB.</summary>
    public const int DefaultResolution = 2048;

    /// <summary>Cascades in the 2x2 atlas.</summary>
    public const int MaxCascades = 4;

    private readonly Renderer _renderer;
    private readonly RenderTarget _target;
    private readonly Matrix4x4[] _lightViewProjection = new Matrix4x4[MaxCascades];
    private readonly Matrix4x4[] _worldToShadow = new Matrix4x4[MaxCascades];
    private readonly Vector4[] _rects = new Vector4[MaxCascades];
    private int _cascadeCount = MaxCascades;
    private bool _disposed;

    /// <summary>Creates the atlas at <paramref name="resolution"/> square. Render thread.</summary>
    public ShadowMap(Renderer renderer, int resolution = DefaultResolution)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentOutOfRangeException.ThrowIfLessThan(resolution, 2);

        _renderer = renderer;
        Resolution = resolution;
        _target = renderer.CreateRenderTarget(RenderTargetDesc.DepthOnly(resolution));

        for (int i = 0; i < MaxCascades; i++)
        {
            _lightViewProjection[i] = Matrix4x4.Identity;
            _worldToShadow[i] = Matrix4x4.Identity;
        }
    }

    /// <summary>Square resolution of the whole atlas.</summary>
    public int Resolution { get; }

    /// <summary>Square resolution of one cascade's quadrant.</summary>
    public int TileResolution => Resolution / 2;

    /// <summary>The target the depth pass draws into.</summary>
    public RenderTarget Target => _target;

    /// <summary>The depth atlas, sampled by the light pass. Never null: the target is depth-only.</summary>
    public Texture Depth => _target.DepthTexture!;

    /// <summary>How many cascades are fitted and drawn. 1 to <see cref="MaxCascades"/>.</summary>
    public int CascadeCount
    {
        get => _cascadeCount;
        set
        {
            _cascadeCount = Math.Clamp(value, 1, MaxCascades);

            // The spans are read before the next Fit and must not cover unfitted slots.
            FittedCascadeCount = Math.Min(FittedCascadeCount, _cascadeCount);
        }
    }

    /// <summary>
    /// How far from the camera shadows are drawn. Beyond it surfaces are lit but
    /// never shadowed. Not the camera's far plane.
    /// </summary>
    public float Distance { get; set; } = 60f;

    /// <summary>
    /// How the range is divided between cascades: 0 splits it evenly, 1
    /// logarithmically.
    /// </summary>
    public float SplitBlend { get; set; } = 0.88f;

    /// <summary>
    /// The rasterizer's depth offset while the map is drawn. This is the acne fix.
    /// </summary>
    // The slope term has to cover the PCF filter's whole footprint, not one texel.
    // Smallest value with no self-shadowing, measured: radius up to 0.8 needs 6,
    // 1.2 needs 8, 2 needs 10, 3 needs 14. Raise it when FilterRadius is widened.
    public DepthBias RasterBias { get; set; } = new(Constant: 2000, SlopeScaled: 8f);

    /// <summary>
    /// Radius of the PCF tap circle, in texels of the chosen cascade. The fetch
    /// count does not depend on it. See <see cref="RasterBias"/> before widening.
    /// </summary>
    public float FilterRadius { get; set; } = 1.2f;

    /// <summary>
    /// Constant subtracted from the compared depth, for what <see cref="RasterBias"/> misses.
    /// Raising it detaches shadows from their casters.
    /// </summary>
    public float CompareBias { get; set; } = 0.0002f;

    /// <summary>World-to-light-clip for one cascade, for the depth pass to draw with.</summary>
    public Matrix4x4 LightViewProjectionAt(int cascade) => _lightViewProjection[cascade];

    /// <summary>
    /// Per cascade: world position to a lookup in that cascade's own 0..1 space,
    /// with z directly comparable against what the map stores. The backend's Y
    /// flip and depth range are already folded in.
    /// </summary>
    public ReadOnlySpan<Matrix4x4> WorldToShadow => _worldToShadow.AsSpan(0, FittedCascadeCount);

    /// <summary>
    /// Per cascade: the atlas quadrant as (u, v, scale), plus that cascade's
    /// world texel size in w.
    /// </summary>
    public ReadOnlySpan<Vector4> CascadeRects => _rects.AsSpan(0, FittedCascadeCount);

    /// <summary>One texel of a cascade, in that cascade's own 0..1 space. The filter kernel's step.</summary>
    public float TexelSize => 1f / TileResolution;

    /// <summary>World-space texel size of the sharpest cascade. Diagnostics.</summary>
    public float WorldTexelSize => _rects[0].W;

    /// <summary>World-space texel size of the coarsest fitted cascade. Diagnostics.</summary>
    public float CoarsestWorldTexelSize => _rects[FittedCascadeCount - 1].W;

    /// <summary>The atlas rectangle cascade <paramref name="cascade"/> is drawn into, in texels.</summary>
    public (int X, int Y, int Size) TileAt(int cascade)
    {
        int tile = TileResolution;
        return ((cascade % 2) * tile, (cascade / 2) * tile, tile);
    }

    /// <summary>
    /// Fits every cascade to its slice of <paramref name="camera"/>'s frustum,
    /// lit from <paramref name="lightDirection"/>.
    /// </summary>
    /// <param name="lightDirection">The direction the light travels, as a <see cref="RenderLight"/> carries it.</param>
    /// <returns>False when nothing could be fitted and no shadow should be drawn.</returns>
    public bool Fit(Camera camera, Vector3 lightDirection)
    {
        ArgumentNullException.ThrowIfNull(camera);

        float near = camera.NearPlane;
        float far = MathF.Min(Distance, camera.FarPlane);

        // An orthographic slab is symmetric about the eye, so centre the range
        // on it. Fitting from the near plane forward would miss what is behind.
        if (camera.ProjectionKind == CameraProjectionKind.Orthographic)
        {
            near = -Distance * 0.5f;
            far = Distance * 0.5f;
        }

        if (far <= near) return false;

        // One cascade under a parallel projection: there is no foreshortening to grade.
        int cascades = camera.ProjectionKind == CameraProjectionKind.Orthographic
            ? 1
            : _cascadeCount;

        Span<float> splits = stackalloc float[MaxCascades];
        ComputeSplits(near, far, cascades, SplitBlend, splits);

        Matrix4x4 ndcToTexture = NdcToShadowTexture(
            _renderer.DepthToNdcZ, _renderer.TargetOriginIsTopLeft);

        float sliceNear = near;
        for (int i = 0; i < cascades; i++)
        {
            float sliceFar = splits[i];

            // Slight overlap, or a receiver on a split can fall outside both cascades and flicker.
            float overlappedNear = i == 0 ? sliceNear : sliceNear * 0.96f;

            if (!TryFitLightMatrix(
                    camera, lightDirection, overlappedNear, sliceFar, TileResolution,
                    out Matrix4x4 lightViewProjection, out float worldTexel))
            {
                return false;
            }

            _lightViewProjection[i] = lightViewProjection;
            _worldToShadow[i] = lightViewProjection * _renderer.ClipZCorrection * ndcToTexture;

            (int x, int y, int size) = TileAt(i);
            _rects[i] = new Vector4(
                x / (float)Resolution, y / (float)Resolution, size / (float)Resolution, worldTexel);

            sliceNear = sliceFar;
        }

        // Slots past this still hold an older fit, so the shader must not read them.
        FittedCascadeCount = cascades;
        return true;
    }

    /// <summary>
    /// How many cascades the last <see cref="Fit"/> produced. Usually
    /// <see cref="CascadeCount"/>; an orthographic camera fits one.
    /// </summary>
    public int FittedCascadeCount { get; private set; } = MaxCascades;

    // Writes where each cascade ends.
    internal static void ComputeSplits(float near, float far, int count, float blend, Span<float> splits)
    {
        // Pow of a negative base is NaN, and an orthographic slab has a negative
        // near. Uniform splits there.
        bool logarithmicUsable = near > 0f && far > 0f;

        for (int i = 1; i <= count; i++)
        {
            float fraction = i / (float)count;
            float uniform = near + (far - near) * fraction;

            if (!logarithmicUsable)
            {
                splits[i - 1] = uniform;
                continue;
            }

            float logarithmic = near * MathF.Pow(far / near, fraction);
            splits[i - 1] = blend * logarithmic + (1f - blend) * uniform;
        }

        // Not whatever the blend rounded it to.
        splits[count - 1] = far;
    }

    // No renderer involved, so tests can call it. False when there is nothing to fit.
    internal static bool TryFitLightMatrix(
        Camera camera,
        Vector3 lightDirection,
        float near,
        float far,
        int resolution,
        out Matrix4x4 lightViewProjection,
        out float worldTexelSize)
    {
        ArgumentNullException.ThrowIfNull(camera);
        lightViewProjection = Matrix4x4.Identity;
        worldTexelSize = 0f;

        if (lightDirection.LengthSquared() < 1e-12f) return false;
        if (far <= near) return false;
        Vector3 forward = Vector3.Normalize(lightDirection);

        BoundSlice(camera, near, far, out Vector3 center, out float radius);
        if (radius <= 0f) return false;

        // Extend toward the light so casters just outside the view still cast into it.
        float casterMargin = radius * 2f;

        // World up fails for a sun pointing straight down.
        Vector3 up = MathF.Abs(forward.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        // Light space is anchored at the world origin, not at the slice centre.
        // A frame that moves with the camera would make the texel snap below useless.
        Matrix4x4 lightView = Matrix4x4.CreateLookAt(Vector3.Zero, forward, up);

        // Snap the centre to whole texels so shadow edges do not shimmer as the
        // camera moves. Needs the constant texel size the bounding sphere gives.
        float diameter = radius * 2f;
        float texelsPerUnit = resolution / diameter;
        Vector3 centerInLight = Vector3.Transform(center, lightView);
        float snappedX = MathF.Floor(centerInLight.X * texelsPerUnit) / texelsPerUnit;
        float snappedY = MathF.Floor(centerInLight.Y * texelsPerUnit) / texelsPerUnit;

        // CreateLookAt is right-handed: in front of the light is negative z, and
        // the ortho planes are forward distances. Depth needs no snap, a z shift
        // moves caster and receiver alike.
        float zNear = -(centerInLight.Z + radius + casterMargin);
        float zFar = -(centerInLight.Z - radius);

        Matrix4x4 lightProjection = Matrix4x4.CreateOrthographicOffCenter(
            snappedX - radius, snappedX + radius,
            snappedY - radius, snappedY + radius,
            zNear, zFar);

        lightViewProjection = lightView * lightProjection;
        worldTexelSize = diameter / resolution;
        return true;
    }

    // NDC xy to texture coordinates, NDC z to what the depth buffer stores.
    // The z row inverts Renderer.DepthToNdcZ; the y sign follows the target origin.
    internal static Matrix4x4 NdcToShadowTexture(Vector2 depthToNdc, bool topLeftOrigin)
    {
        float zScale = 1f / depthToNdc.X;
        float zBias = -depthToNdc.Y / depthToNdc.X;
        float ySign = topLeftOrigin ? -0.5f : 0.5f;

        return new Matrix4x4(
            0.5f, 0f, 0f, 0f,
            0f, ySign, 0f, 0f,
            0f, 0f, zScale, 0f,
            0.5f, 0.5f, zBias, 1f);
    }

    // A sphere, not a box: a box changes size when the camera turns, and the
    // texel snap needs a constant size.
    private static void BoundSlice(Camera camera, float near, float far, out Vector3 center, out float radius)
    {
        float tanHalfFov = MathF.Tan(camera.FieldOfView * 0.5f);
        Vector3 position = camera.Position;
        Vector3 forward = camera.Forward;
        Vector3 right = camera.Right;
        Vector3 up = camera.Up;

        // Orthographic: same half extents at both ends.
        bool orthographic = camera.ProjectionKind == CameraProjectionKind.Orthographic;
        float orthoHalfHeight = camera.OrthographicHeight * 0.5f;

        Span<Vector3> corners = stackalloc Vector3[8];
        int c = 0;
        for (int end = 0; end < 2; end++)
        {
            float distance = end == 0 ? near : far;
            float halfHeight = orthographic ? orthoHalfHeight : distance * tanHalfFov;
            float halfWidth = halfHeight * camera.AspectRatio;
            Vector3 middle = position + forward * distance;

            corners[c++] = middle - right * halfWidth - up * halfHeight;
            corners[c++] = middle + right * halfWidth - up * halfHeight;
            corners[c++] = middle - right * halfWidth + up * halfHeight;
            corners[c++] = middle + right * halfWidth + up * halfHeight;
        }

        center = Vector3.Zero;
        for (int i = 0; i < corners.Length; i++)
            center += corners[i];
        center /= corners.Length;

        float furthest = 0f;
        for (int i = 0; i < corners.Length; i++)
            furthest = MathF.Max(furthest, Vector3.DistanceSquared(corners[i], center));

        // A little slack: the snap moves the box by up to a texel.
        radius = MathF.Sqrt(furthest) * 1.02f;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.DestroyRenderTarget(_target);
    }
}
