using System;
using System.Numerics;
using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// What a brush face wears: a material and Hammer-style texture axes.
/// For a world-space vertex p, <c>u = dot(p, UAxis) / UScale + UOffset</c>,
/// and likewise for v. Zero axes mean world-aligned: the axes are derived from
/// the face normal by the dominant-axis rule and stay on the world grid when
/// the brush moves. Stored axes are texture-locked through rigid transforms.
/// </summary>
// A plain value with an interned material id, no asset objects: it rides
// through the background compile and resolves on the render thread.
// Texture rotation is not stored; it is the axes rotated about the normal.
public readonly struct FaceSurface : IEquatable<FaceSurface>
{
    /// <summary>Default material, world-aligned, scale 1, no offset.</summary>
    public static readonly FaceSurface Default = new(MaterialRef.Default);

    /// <summary>A world-aligned face wearing <paramref name="material"/>, scale 1, no offset.</summary>
    public FaceSurface(MaterialRef material)
        : this(material, Vector3.Zero, Vector3.Zero, 0f, 0f, 1f, 1f)
    {
    }

    /// <summary>
    /// A face with explicit texture axes. Zero axes give a world-aligned face.
    /// Throws on a zero or non-finite scale.
    /// </summary>
    public FaceSurface(
        MaterialRef material,
        Vector3 uAxis,
        Vector3 vAxis,
        float uOffset,
        float vOffset,
        float uScale,
        float vScale)
    {
        ThrowIfUnusableScale(uScale, nameof(uScale));
        ThrowIfUnusableScale(vScale, nameof(vScale));

        Material = material;
        UAxis = uAxis;
        VAxis = vAxis;
        UOffset = uOffset;
        VOffset = vOffset;
        UScale = uScale;
        VScale = vScale;
    }

    /// <summary>The material this face wears, as an interned id.</summary>
    public MaterialRef Material { get; }

    /// <summary>Direction the U coordinate runs along. Zero means world-aligned.</summary>
    public Vector3 UAxis { get; }

    /// <summary>Direction the V coordinate runs along. Zero means world-aligned.</summary>
    public Vector3 VAxis { get; }

    /// <summary>U shift in texture repeats, added after the scale division.</summary>
    public float UOffset { get; }

    /// <summary>V shift in texture repeats, added after the scale division.</summary>
    public float VOffset { get; }

    /// <summary>World units per texture repeat along <see cref="UAxis"/>.</summary>
    public float UScale { get; }

    /// <summary>World units per texture repeat along <see cref="VAxis"/>.</summary>
    public float VScale { get; }

    /// <summary>
    /// True when the axes are derived from the face normal rather than stored.
    /// One zero axis is enough.
    /// </summary>
    public bool IsWorldAligned =>
        UAxis == Vector3.Zero || VAxis == Vector3.Zero;

    /// <summary>Returns this face with a different material and the same texture mapping.</summary>
    public FaceSurface WithMaterial(MaterialRef material) =>
        new(material, UAxis, VAxis, UOffset, VOffset, EffectiveScale(UScale), EffectiveScale(VScale));

    /// <summary>
    /// Returns this face with different texture axes and the same material.
    /// Pass zero axes to go back to world alignment.
    /// </summary>
    public FaceSurface WithAxes(Vector3 uAxis, Vector3 vAxis, float uOffset, float vOffset, float uScale, float vScale) =>
        new(Material, uAxis, vAxis, uOffset, vOffset, uScale, vScale);

    /// <summary>
    /// Maps the face through a rigid <paramref name="transform"/>. A
    /// world-aligned face is returned unchanged; stored axes are texture-locked.
    /// </summary>
    // Lock: with p' = R*p + t, U' = R*U and o' = o - dot(t, U')/s, so a moved
    // point keeps its UV. Non-rigid input is out of contract; not renormalized.
    public FaceSurface Transformed(in Matrix4x4 transform)
    {
        if (IsWorldAligned)
            return this;

        Vector3 u = Vector3.TransformNormal(UAxis, transform);
        Vector3 v = Vector3.TransformNormal(VAxis, transform);
        Vector3 translation = transform.Translation;

        float uScale = EffectiveScale(UScale);
        float vScale = EffectiveScale(VScale);
        return new FaceSurface(
            Material,
            u, v,
            UOffset - Vector3.Dot(translation, u) / uScale,
            VOffset - Vector3.Dot(translation, v) / vScale,
            uScale, vScale);
    }

    /// <summary>
    /// The axes and scales to generate UVs with: the stored ones, or for a
    /// world-aligned face the two world axes orthogonal to the dominant
    /// component of <paramref name="faceNormal"/>.
    /// </summary>
    // The mapping and its >= tie-breaks are pinned by FaceSurfaceTests;
    // changing either moves the UVs of every default brush.
    public void ResolveAxes(Vector3 faceNormal, out Vector3 uAxis, out Vector3 vAxis, out float uScale, out float vScale)
    {
        uScale = EffectiveScale(UScale);
        vScale = EffectiveScale(VScale);

        if (!IsWorldAligned)
        {
            uAxis = UAxis;
            vAxis = VAxis;
            return;
        }

        float ax = MathF.Abs(faceNormal.X), ay = MathF.Abs(faceNormal.Y), az = MathF.Abs(faceNormal.Z);
        if (ax >= ay && ax >= az)
        {
            uAxis = Vector3.UnitZ;
            vAxis = Vector3.UnitY;
        }
        else if (ay >= az)
        {
            uAxis = Vector3.UnitX;
            vAxis = Vector3.UnitZ;
        }
        else
        {
            uAxis = Vector3.UnitX;
            vAxis = Vector3.UnitY;
        }
    }

    /// <summary>The UV of one world-space point on a face with the given world-space normal.</summary>
    public Vector2 ComputeUv(Vector3 worldPosition, Vector3 faceNormal)
    {
        ResolveAxes(faceNormal, out Vector3 u, out Vector3 v, out float uScale, out float vScale);
        return new Vector2(
            Vector3.Dot(worldPosition, u) / uScale + UOffset,
            Vector3.Dot(worldPosition, v) / vScale + VOffset);
    }

    /// <inheritdoc/>
    // Effective scales, so default(FaceSurface) equals Default.
    public bool Equals(FaceSurface other) =>
        Material == other.Material &&
        UAxis == other.UAxis &&
        VAxis == other.VAxis &&
        UOffset == other.UOffset &&
        VOffset == other.VOffset &&
        EffectiveScale(UScale) == EffectiveScale(other.UScale) &&
        EffectiveScale(VScale) == EffectiveScale(other.VScale);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FaceSurface other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Material.Id);
        hash.Add(UAxis);
        hash.Add(VAxis);
        hash.Add(UOffset);
        hash.Add(VOffset);
        hash.Add(EffectiveScale(UScale));
        hash.Add(EffectiveScale(VScale));
        return hash.ToHashCode();
    }

    public static bool operator ==(FaceSurface left, FaceSurface right) => left.Equals(right);

    public static bool operator !=(FaceSurface left, FaceSurface right) => !left.Equals(right);

    // default(FaceSurface) has zero scales; treat it as the default scale of 1.
    private static float EffectiveScale(float scale) => scale != 0f ? scale : 1f;

    private static void ThrowIfUnusableScale(float scale, string paramName)
    {
        if (scale == 0f || !float.IsFinite(scale))
            throw new ArgumentOutOfRangeException(
                paramName, scale, "Texture scale must be a non-zero finite number (world units per texture repeat).");
    }
}
