using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// A convex solid: the intersection of half-spaces, one outward-facing plane
/// per face. The authoring primitive for static world geometry. Immutable
/// after construction; the <c>With*</c> methods return a successor.
/// </summary>
// Planes and faces are in the brush's local frame, so CSG precision does not
// depend on how far the brush is from the world origin.
// The carve cache keys on Brush reference identity. A changed brush must be a
// new instance and an unchanged one must stay the same instance.
public sealed class Brush
{
    private const float SeedExtentScale = 100f;
    private const float SeedExtentFloor = 1f;

    // Same as Csg's coplanarity tolerances at carve time.
    private const float DuplicateNormalEpsilon = 1e-4f;
    private const float DuplicateOffsetEpsilon = 1e-3f;

    // Boundedness test: build the faces again with the seed scaled by this.
    // Non-integer so vertices of the two builds can't coincide by accident.
    private const float SeedProbeFactor = 1.5f;

    // Allowed drift between the two builds, as a fraction of the seed extent.
    private const float SeedComparisonTolerance = 1e-3f;

    private readonly Plane[] _localPlanes;
    private readonly Polygon[] _localFaces;

    // _localFaces index per plane, or -1 when the plane's face clipped away.
    private readonly int[] _faceIndexByPlane;
    private readonly FaceSurface[] _faceSurfaces;

    public Brush(IReadOnlyList<Plane> localPlanes)
        : this(localPlanes, Matrix4x4.Identity, faceSurfaces: null)
    {
    }

    public Brush(IReadOnlyList<Plane> localPlanes, Matrix4x4 transform)
        : this(localPlanes, transform, faceSurfaces: null)
    {
    }

    /// <summary>
    /// Builds a brush with a material and texture axes per bounding plane.
    /// </summary>
    /// <param name="faceSurfaces">
    /// One per plane, in plane order, or null for <see cref="FaceSurface.Default"/>
    /// on every face. The array is copied.
    /// </param>
    public Brush(
        IReadOnlyList<Plane> localPlanes,
        Matrix4x4 transform,
        IReadOnlyList<FaceSurface>? faceSurfaces,
        BrushOperation operation = BrushOperation.Additive)
    {
        Operation = operation;
        if (localPlanes.Count < 4)
            throw new ArgumentException("A brush needs at least 4 planes to bound a volume.", nameof(localPlanes));
        if (faceSurfaces is not null && faceSurfaces.Count != localPlanes.Count)
            throw new ArgumentException(
                $"Expected one face surface per plane ({localPlanes.Count}), got {faceSurfaces.Count}.",
                nameof(faceSurfaces));

        _localPlanes = new Plane[localPlanes.Count];
        for (int i = 0; i < localPlanes.Count; i++)
            _localPlanes[i] = Plane.Normalize(localPlanes[i]);

        RejectDuplicatePlanes(_localPlanes, nameof(localPlanes));

        _faceSurfaces = new FaceSurface[_localPlanes.Length];
        for (int i = 0; i < _faceSurfaces.Length; i++)
            _faceSurfaces[i] = faceSurfaces is null ? FaceSurface.Default : faceSurfaces[i];

        Transform = transform;

        float seedExtent = ComputeSeedExtent(_localPlanes);
        Polygon?[] facesByPlane = BuildFaces(_localPlanes, _faceSurfaces, seedExtent);
        RejectUnboundedVolume(_localPlanes, _faceSurfaces, facesByPlane, seedExtent, nameof(localPlanes));
        _localFaces = CollectFaces(facesByPlane, out _faceIndexByPlane);

        if (_localFaces.Length == 0)
            throw new ArgumentException(
                "The planes clip every face away — they do not form a closed solid.", nameof(localPlanes));

        LocalBounds = ComputeBounds(_localFaces);
    }

    // Shares the arrays: no plane moved and everything in them is immutable.
    // Transform is copied because the standalone carve path reads it.
    private Brush(Brush source, BrushOperation operation)
    {
        _localPlanes = source._localPlanes;
        _localFaces = source._localFaces;
        _faceIndexByPlane = source._faceIndexByPlane;
        _faceSurfaces = source._faceSurfaces;
        LocalBounds = source.LocalBounds;
        Transform = source.Transform;
        Operation = operation;
    }

    /// <summary>
    /// Whether this brush adds solid to the compiled world or removes it.
    /// Change it with <see cref="WithOperation"/>.
    /// </summary>
    public BrushOperation Operation { get; }

    /// <summary>
    /// Returns a copy with a different <see cref="BrushOperation"/>, or this
    /// brush when it already has it.
    /// </summary>
    public Brush WithOperation(BrushOperation operation) =>
        operation == Operation ? this : new Brush(this, operation);

    /// <summary>A new <see cref="Brush"/> instance describing the same solid.</summary>
    // A duplicated node needs its own instance: the carve cache holds one
    // entry per reference, and Transform is mutable.
    public Brush CloneShape() => new(this, Operation);

    /// <summary>
    /// World-from-local transform for standalone brushes (tests, tools). A
    /// brush on a scene node ignores it and is placed by the node's world
    /// matrix. Keep it rigid: size belongs in the plane offsets.
    /// </summary>
    public Matrix4x4 Transform { get; set; }

    /// <summary>Outward-facing planes in the brush's local frame.</summary>
    public IReadOnlyList<Plane> LocalPlanes => _localPlanes;

    /// <summary>
    /// The clipped face polygons in the brush's local frame. Not indexed by
    /// plane: a plane whose face clipped away contributes none.
    /// </summary>
    public IReadOnlyList<Polygon> LocalFaces => _localFaces;

    /// <summary>The per-face surfaces, in the same order as <see cref="LocalPlanes"/>.</summary>
    public IReadOnlyList<FaceSurface> FaceSurfaces => _faceSurfaces;

    /// <summary>Axis-aligned bounding box in the brush's local frame.</summary>
    public Aabb LocalBounds { get; }

    /// <summary>Axis-aligned bounding box in world space, under <see cref="Transform"/>.</summary>
    public Aabb WorldBounds => LocalBounds.Transform(Transform);

    /// <summary>
    /// Builds an axis-aligned box brush spanning <paramref name="min"/>..<paramref name="max"/>.
    /// The planes are centred on the local origin and the centre goes into
    /// <see cref="Transform"/>. For a brush on a scene node pass node-local
    /// extents, since the node places it.
    /// </summary>
    public static Brush CreateBox(Vector3 min, Vector3 max)
        => CreateBox(min, max, MaterialRef.Default);

    /// <summary>
    /// <see cref="CreateBox(Vector3, Vector3)"/> with every face wearing
    /// <paramref name="material"/>, world-aligned. Face order is fixed:
    /// +X, -X, +Y, -Y, +Z, -Z.
    /// </summary>
    public static Brush CreateBox(Vector3 min, Vector3 max, MaterialRef material)
    {
        Vector3 halfExtent = (max - min) * 0.5f;
        Vector3 center = (min + max) * 0.5f;

        Plane[] localPlanes =
        [
            new(new Vector3(1f, 0f, 0f), -halfExtent.X),
            new(new Vector3(-1f, 0f, 0f), -halfExtent.X),
            new(new Vector3(0f, 1f, 0f), -halfExtent.Y),
            new(new Vector3(0f, -1f, 0f), -halfExtent.Y),
            new(new Vector3(0f, 0f, 1f), -halfExtent.Z),
            new(new Vector3(0f, 0f, -1f), -halfExtent.Z),
        ];

        FaceSurface[]? faces = null;
        if (!material.IsDefault)
        {
            faces = new FaceSurface[localPlanes.Length];
            for (int i = 0; i < faces.Length; i++)
                faces[i] = new FaceSurface(material);
        }

        return new Brush(localPlanes, Matrix4x4.CreateTranslation(center), faces);
    }

    /// <summary>
    /// Returns a copy of this brush with plane <paramref name="planeIndex"/>'s
    /// face wearing <paramref name="material"/>. Assign the copy back to the
    /// scene node to recompile.
    /// </summary>
    public Brush WithFaceMaterial(int planeIndex, MaterialRef material)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(planeIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(planeIndex, _faceSurfaces.Length);
        return WithFaceSurface(planeIndex, _faceSurfaces[planeIndex].WithMaterial(material));
    }

    /// <summary>
    /// The polygon plane <paramref name="planeIndex"/> produced. False when
    /// the other planes clipped it away, which is not an error.
    /// </summary>
    public bool TryGetPlaneFace(int planeIndex, out Polygon face)
    {
        if (planeIndex < 0 || planeIndex >= _faceIndexByPlane.Length)
        {
            face = default!;
            return false;
        }

        int index = _faceIndexByPlane[planeIndex];
        if (index < 0)
        {
            face = default!;
            return false;
        }

        face = _localFaces[index];
        return true;
    }

    /// <summary>
    /// Returns a copy of this brush with every face wearing
    /// <paramref name="material"/>, or this brush when they all already do.
    /// </summary>
    public Brush WithAllFacesMaterial(MaterialRef material)
    {
        bool changed = false;
        for (int i = 0; i < _faceSurfaces.Length; i++)
        {
            if (!_faceSurfaces[i].Material.Equals(material))
            {
                changed = true;
                break;
            }
        }

        if (!changed) return this;

        var faces = new FaceSurface[_faceSurfaces.Length];
        for (int i = 0; i < faces.Length; i++)
            faces[i] = _faceSurfaces[i].WithMaterial(material);

        return new Brush(_localPlanes, Transform, faces, Operation);
    }

    /// <summary>
    /// Returns a copy of this brush with plane <paramref name="planeIndex"/>'s
    /// material and texture axes replaced.
    /// </summary>
    public Brush WithFaceSurface(int planeIndex, FaceSurface face)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(planeIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(planeIndex, _faceSurfaces.Length);

        var faces = new FaceSurface[_faceSurfaces.Length];
        Array.Copy(_faceSurfaces, faces, faces.Length);
        faces[planeIndex] = face;
        // Pass Operation, or retexturing a hole makes it solid.
        return new Brush(_localPlanes, Transform, faces, Operation);
    }

    /// <summary>
    /// Returns a copy of this brush scaled by <paramref name="scale"/> about
    /// its local origin. This is how a brush is resized, since node transforms
    /// must stay rigid. Works for any convex shape, not only boxes, and
    /// rebuilds the brush, so call it only when the extents changed.
    /// </summary>
    /// <param name="scale">Per-axis factors. Each must be finite and positive.</param>
    // Half-space {n.p + d <= 0} under p -> S.p becomes n' = normalize(S^-1 n),
    // d' = d / |S^-1 n|. Face surfaces carry over, so explicit texture axes
    // don't stretch.
    public Brush WithScaledExtents(Vector3 scale)
    {
        ThrowIfUnusableScale(scale.X, nameof(scale));
        ThrowIfUnusableScale(scale.Y, nameof(scale));
        ThrowIfUnusableScale(scale.Z, nameof(scale));

        if (scale == Vector3.One)
            return this;

        var planes = new Plane[_localPlanes.Length];
        for (int i = 0; i < planes.Length; i++)
        {
            Plane plane = _localPlanes[i];
            var mapped = new Vector3(
                plane.Normal.X / scale.X,
                plane.Normal.Y / scale.Y,
                plane.Normal.Z / scale.Z);

            // Non-zero: the source normal is unit length and the scale is positive.
            float length = mapped.Length();
            planes[i] = new Plane(mapped / length, plane.D / length);
        }

        // Pass Operation, or resizing a hole makes it solid.
        return new Brush(planes, Transform, _faceSurfaces, Operation);
    }

    private static void ThrowIfUnusableScale(float component, string paramName)
    {
        if (!float.IsFinite(component) || component <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                paramName, component,
                "Brush extent scale factors must be finite and strictly positive; " +
                "zero collapses the solid and a negative factor turns it inside out.");
        }
    }

    // Two same-facing near-coincident planes make Polygon.Split drop both
    // faces in BuildFaces, leaving an open solid.
    private static void RejectDuplicatePlanes(Plane[] planes, string paramName)
    {
        for (int i = 0; i < planes.Length; i++)
        {
            for (int j = i + 1; j < planes.Length; j++)
            {
                if (Vector3.Dot(planes[i].Normal, planes[j].Normal) > 1f - DuplicateNormalEpsilon &&
                    MathF.Abs(planes[i].D - planes[j].D) < DuplicateOffsetEpsilon)
                {
                    throw new ArgumentException(
                        $"Planes {i} and {j} are near-coplanar duplicates (same facing, similar offset); " +
                        "each brush face needs a distinct bounding plane.", paramName);
                }
            }
        }
    }

    // A bounded face's vertices don't depend on the seed quad. An unbounded
    // face keeps vertices on the seed boundary, so it moves when the seed
    // grows. A vertex-magnitude threshold can't tell: a sharp spire is far
    // from the origin and still bounded.
    private static void RejectUnboundedVolume(
        Plane[] planes, FaceSurface[] faceSurfaces, Polygon?[] facesByPlane, float seedExtent, string paramName)
    {
        Polygon?[] probe = BuildFaces(planes, faceSurfaces, seedExtent * SeedProbeFactor);
        float tolerance = seedExtent * SeedComparisonTolerance;

        for (int i = 0; i < facesByPlane.Length; i++)
        {
            Polygon? face = facesByPlane[i];
            Polygon? probeFace = probe[i];

            if (face is null && probeFace is null)
                continue;

            if (face is null || probeFace is null ||
                face.VertexCount != probeFace.VertexCount ||
                MathF.Abs(MaxVertexMagnitude(face) - MaxVertexMagnitude(probeFace)) > tolerance)
            {
                throw new ArgumentException(
                    "The planes do not enclose a bounded volume.", paramName);
            }
        }
    }

    private static float MaxVertexMagnitude(Polygon face)
    {
        float max = 0f;
        foreach (Vector3 v in face.Vertices)
            max = MathF.Max(max, v.Length());
        return max;
    }

    // Clips a seed quad per plane against every other plane, keeping the back
    // side. Indexed by plane, null where the face clipped away.
    private static Polygon?[] BuildFaces(Plane[] planes, FaceSurface[] faceSurfaces, float seedExtent)
    {
        var faces = new Polygon?[planes.Length];

        for (int i = 0; i < planes.Length; i++)
        {
            Polygon? face = CreatePlaneQuad(planes[i], faceSurfaces[i], seedExtent);

            for (int j = 0; j < planes.Length && face is not null; j++)
            {
                if (j == i) continue;
                face.Split(planes[j], out _, out Polygon? inside);
                face = inside;
            }

            faces[i] = face;
        }

        return faces;
    }

    private static Polygon[] CollectFaces(Polygon?[] facesByPlane, out int[] faceIndexByPlane)
    {
        var faces = new List<Polygon>(facesByPlane.Length);
        faceIndexByPlane = new int[facesByPlane.Length];

        for (int i = 0; i < facesByPlane.Length; i++)
        {
            Polygon? face = facesByPlane[i];
            if (face is null)
            {
                faceIndexByPlane[i] = -1;
                continue;
            }

            faceIndexByPlane[i] = faces.Count;
            faces.Add(face);
        }

        return faces.ToArray();
    }

    // Sized to the brush, not a fixed large value: a smaller seed means less
    // float noise in every split.
    private static float ComputeSeedExtent(Plane[] planes)
    {
        float maxOffset = 0f;
        foreach (Plane p in planes)
            maxOffset = MathF.Max(maxOffset, MathF.Abs(p.D));
        return MathF.Max(maxOffset * SeedExtentScale, SeedExtentFloor);
    }

    private static Polygon CreatePlaneQuad(Plane plane, FaceSurface face, float seedExtent)
    {
        Vector3 normal = plane.Normal;
        Vector3 reference = MathF.Abs(normal.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(reference, normal));
        Vector3 bitangent = Vector3.Cross(normal, tangent);
        Vector3 center = normal * -plane.D;

        Vector3[] verts =
        [
            center - tangent * seedExtent - bitangent * seedExtent,
            center + tangent * seedExtent - bitangent * seedExtent,
            center + tangent * seedExtent + bitangent * seedExtent,
            center - tangent * seedExtent + bitangent * seedExtent,
        ];
        return new Polygon(verts, plane, face);
    }

    private static Aabb ComputeBounds(Polygon[] faces)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (Polygon face in faces)
        {
            foreach (Vector3 v in face.Vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        }
        return new Aabb(min, max);
    }

}
