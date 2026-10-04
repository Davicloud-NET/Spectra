using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// A validated <c>.smodel</c>, as spans into the bytes it was read from. Nothing
/// is copied.
/// </summary>
// ref struct so it cannot outlive the buffer, which is usually a mapped pack
// view. A span into an unmapped view is an access violation, not an exception.
public readonly ref struct SmodelModel
{
    /// <summary>The file's name, for messages.</summary>
    public readonly string Source;

    /// <summary>Whole-model properties as declared in the header.</summary>
    public readonly SmodelFlags Flags;

    /// <summary>
    /// The header's vertex layout id, checked against <c>VTXL</c> at read.
    /// Compare it with the renderer's layout to decide whether
    /// <see cref="Vertices"/> can be uploaded as is.
    /// </summary>
    public readonly uint VertexLayoutId;

    /// <summary>Model-local minimum corner.</summary>
    public readonly Vector3 BoundsMin;

    /// <summary>Model-local maximum corner.</summary>
    public readonly Vector3 BoundsMax;

    /// <summary>The layout <see cref="Vertices"/> is interleaved in.</summary>
    public readonly ReadOnlySpan<SmodelVertexAttribute> VertexAttributes;

    /// <summary>Floats per vertex.</summary>
    public readonly uint VertexStrideFloats;

    /// <summary>The one interleaved vertex buffer, shared by every submesh and every LOD.</summary>
    public readonly ReadOnlySpan<float> Vertices;

    /// <summary>The index buffer when it is 16-bit; empty when it is not.</summary>
    public readonly ReadOnlySpan<ushort> Indices16;

    /// <summary>The index buffer when it is 32-bit; empty when it is not.</summary>
    public readonly ReadOnlySpan<uint> Indices32;

    /// <summary>Submeshes, as index ranges into the one index buffer.</summary>
    public readonly ReadOnlySpan<SmodelSubmesh> Submeshes;

    /// <summary>Levels of detail, as submesh ranges. Empty when the model has none.</summary>
    public readonly ReadOnlySpan<SmodelLod> Lods;

    /// <summary>Skeleton joints in parent-before-child order. Empty when there is no skeleton.</summary>
    public readonly ReadOnlySpan<SmodelJoint> Joints;

    /// <summary>Collision hulls. Empty when the model carries no collision.</summary>
    public readonly ReadOnlySpan<SmodelCollisionHull> CollisionHulls;

    /// <summary>
    /// Every collision plane of every hull, flat. Use <see cref="PlanesOf"/> to
    /// get one hull's planes.
    /// </summary>
    public readonly ReadOnlySpan<Plane> CollisionPlanes;

    /// <summary>The string blob every name offset indexes. Empty when there is none.</summary>
    public readonly ReadOnlySpan<byte> Names;

    /// <summary>How many sections the reader did not recognise and skipped.</summary>
    public readonly int SkippedSectionCount;

    internal SmodelModel(
        string source,
        SmodelFlags flags,
        uint vertexLayoutId,
        Vector3 boundsMin,
        Vector3 boundsMax,
        ReadOnlySpan<SmodelVertexAttribute> vertexAttributes,
        uint vertexStrideFloats,
        ReadOnlySpan<float> vertices,
        ReadOnlySpan<ushort> indices16,
        ReadOnlySpan<uint> indices32,
        ReadOnlySpan<SmodelSubmesh> submeshes,
        ReadOnlySpan<SmodelLod> lods,
        ReadOnlySpan<SmodelJoint> joints,
        ReadOnlySpan<SmodelCollisionHull> collisionHulls,
        ReadOnlySpan<Plane> collisionPlanes,
        ReadOnlySpan<byte> names,
        int skippedSectionCount)
    {
        Source = source;
        Flags = flags;
        VertexLayoutId = vertexLayoutId;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        VertexAttributes = vertexAttributes;
        VertexStrideFloats = vertexStrideFloats;
        Vertices = vertices;
        Indices16 = indices16;
        Indices32 = indices32;
        Submeshes = submeshes;
        Lods = lods;
        Joints = joints;
        CollisionHulls = collisionHulls;
        CollisionPlanes = collisionPlanes;
        Names = names;
        SkippedSectionCount = skippedSectionCount;
    }

    /// <summary>Whether the index buffer is 32-bit.</summary>
    public bool Index32 => (Flags & SmodelFlags.Index32) != 0;

    /// <summary>How many vertices the buffer holds.</summary>
    public int VertexCount => VertexStrideFloats == 0 ? 0 : Vertices.Length / (int)VertexStrideFloats;

    /// <summary>How many indices the buffer holds, whichever width it is.</summary>
    public int IndexCount => Index32 ? Indices32.Length : Indices16.Length;

    /// <summary>Whether a skeleton is present.</summary>
    public bool HasSkeleton => !Joints.IsEmpty;

    /// <summary>Whether collision hulls are present.</summary>
    public bool HasCollision => !CollisionHulls.IsEmpty;

    /// <summary>One index, widened to <c>uint</c> whichever width the file stores.</summary>
    public uint IndexAt(int index) => Index32 ? Indices32[index] : Indices16[index];

    /// <summary>The planes bounding one hull.</summary>
    public ReadOnlySpan<Plane> PlanesOf(in SmodelCollisionHull hull) =>
        CollisionPlanes.Slice((int)hull.PlaneStart, (int)hull.PlaneCount);

    /// <summary>
    /// The name at <paramref name="nameOffset"/>, or the empty string when the
    /// offset is <see cref="SmodelFormat.NameOffsetAbsent"/>.
    /// </summary>
    /// <exception cref="SmodelFormatException">The offset is not a name record.</exception>
    public string GetName(uint nameOffset)
    {
        if (nameOffset == SmodelFormat.NameOffsetAbsent) return string.Empty;

        SmodelReader.RequireNameRecord(Source, Names, nameOffset, "a name offset");

        ReadOnlySpan<byte> record = Names[(int)nameOffset..];
        ushort length = BinaryPrimitives.ReadUInt16LittleEndian(record);
        return Encoding.UTF8.GetString(record.Slice(sizeof(ushort), length));
    }
}
