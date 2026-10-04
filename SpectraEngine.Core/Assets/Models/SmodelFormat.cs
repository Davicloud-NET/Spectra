using System;

namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// The byte layout of a <c>.smodel</c> file, shared by the cook rule that
/// writes one and the reader.
/// </summary>
public static class SmodelFormat
{
    /// <summary>The cooked extension, dot included.</summary>
    public const string FileExtension = ".smodel";

    /// <summary>
    /// File magic, <c>"SMDL"</c>, as a little-endian <see cref="uint"/>: the
    /// first four bytes on disk read <c>S M D L</c>.
    /// </summary>
    public const uint Magic = 'S' | ('M' << 8) | ('D' << 16) | ((uint)'L' << 24);

    /// <summary>
    /// Bytes in the header at offset 0. Fields run to 0x2C; the rest is reserved
    /// and zero-filled.
    /// </summary>
    public const int HeaderSize = 64;

    /// <summary>Absolute offset of the first section-table record.</summary>
    public const int SectionTableOffset = HeaderSize;

    /// <summary>Bytes in one section-table record.</summary>
    public const int SectionSize = 24;

    /// <summary>The smallest file the reader will look at: a header.</summary>
    public const int MinimumFileSize = HeaderSize;

    /// <summary>
    /// Alignment every section starts on. The reader checks it, because the
    /// payloads are cast in place to <c>float</c>, <c>uint</c> and
    /// <see cref="System.Numerics.Plane"/>.
    /// </summary>
    public const int PayloadAlignment = 16;

    /// <summary>Bytes in one <see cref="SmodelVertexAttribute"/> record.</summary>
    public const int VertexAttributeSize = 8;

    /// <summary>Bytes of fixed preamble in <c>VTXL</c>: attribute count and stride.</summary>
    public const int VertexLayoutPreambleSize = 8;

    /// <summary>Bytes in one <see cref="SmodelSubmesh"/> record.</summary>
    public const int SubmeshSize = 40;

    /// <summary>Bytes in one <see cref="SmodelLod"/> record.</summary>
    public const int LodSize = 12;

    /// <summary>Bytes in one <see cref="SmodelJoint"/> record.</summary>
    public const int JointSize = 56;

    /// <summary>Bytes in one <see cref="SmodelCollisionHull"/> record.</summary>
    public const int CollisionHullSize = 8;

    /// <summary>Bytes of fixed preamble in <c>COLL</c>: the hull count.</summary>
    public const int CollisionPreambleSize = 4;

    /// <summary>Bytes in one collision plane, which is a <c>System.Numerics.Plane</c>.</summary>
    public const int CollisionPlaneSize = 16;

    /// <summary>
    /// The fewest planes a hull may carry: the fewest <c>Brush</c>'s constructor
    /// accepts.
    /// </summary>
    public const int MinimumHullPlanes = 4;

    /// <summary>
    /// What a name offset holds when a record has no name. Not zero, which is
    /// the first record's offset. Same value as
    /// <see cref="Packs.PackFormat.NameOffsetAbsent"/>.
    /// </summary>
    public const uint NameOffsetAbsent = 0xFFFFFFFFu;

    /// <summary>Section <c>VTXL</c>: the vertex layout the file's vertices are in.</summary>
    public const uint VertexLayoutSection = 'V' | ('T' << 8) | ('X' << 16) | ((uint)'L' << 24);

    /// <summary>Section <c>VBUF</c>: one interleaved vertex buffer for the whole model.</summary>
    public const uint VertexBufferSection = 'V' | ('B' << 8) | ('U' << 16) | ((uint)'F' << 24);

    /// <summary>Section <c>IBUF</c>: one index buffer for the whole model.</summary>
    public const uint IndexBufferSection = 'I' | ('B' << 8) | ('U' << 16) | ((uint)'F' << 24);

    /// <summary>Section <c>SUBM</c>: submeshes, as index ranges into <c>IBUF</c>.</summary>
    public const uint SubmeshSection = 'S' | ('U' << 8) | ('B' << 16) | ((uint)'M' << 24);

    /// <summary>Section <c>LODS</c>: levels of detail, as submesh ranges.</summary>
    public const uint LodSection = 'L' | ('O' << 8) | ('D' << 16) | ((uint)'S' << 24);

    /// <summary>Section <c>SKEL</c>: the joint hierarchy and its inverse bind matrices.</summary>
    public const uint SkeletonSection = 'S' | ('K' << 8) | ('E' << 16) | ((uint)'L' << 24);

    /// <summary>Section <c>COLL</c>: collision as convex hulls expressed as plane sets.</summary>
    public const uint CollisionSection = 'C' | ('O' << 8) | ('L' << 16) | ((uint)'L' << 24);

    /// <summary>Section <c>NAME</c>: the string blob every name offset indexes.</summary>
    public const uint NameSection = 'N' | ('A' << 8) | ('M' << 16) | ((uint)'E' << 24);

    /// <summary>
    /// Section <c>ANIM</c>. Reserved and never written: animation clips live in
    /// a separate file.
    /// </summary>
    public const uint AnimationSection = 'A' | ('N' << 8) | ('I' << 16) | ((uint)'M' << 24);

    /// <summary>
    /// The layout id a header stamps: FNV-1a over each attribute's
    /// <c>(semantic, component count)</c> pair, in declaration order. Offsets
    /// and stride are not hashed.
    /// </summary>
    public static uint ComputeVertexLayoutId(ReadOnlySpan<SmodelVertexAttribute> attributes)
    {
        const uint offsetBasis = 2166136261u;
        const uint prime = 16777619u;

        uint hash = offsetBasis;
        for (int i = 0; i < attributes.Length; i++)
        {
            hash = (hash ^ (byte)attributes[i].Semantic) * prime;
            hash = (hash ^ attributes[i].ComponentCount) * prime;
        }

        return hash;
    }

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of
    /// <paramref name="alignment"/>, which must be a power of two.
    /// </summary>
    public static long AlignUp(long value, int alignment) => Packs.PackFormat.AlignUp(value, alignment);

    /// <summary>
    /// Renders a FourCC as four characters for a message. Non-printable bytes
    /// become <c>?</c>.
    /// </summary>
    public static string DescribeFourCc(uint fourCc)
    {
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            char c = (char)((fourCc >> (i * 8)) & 0xFF);
            chars[i] = c is >= ' ' and <= '~' ? c : '?';
        }

        return new string(chars);
    }

    /// <summary>
    /// Throws on a big-endian machine. The payloads are cast in place, so there
    /// is no byte-swapping path.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">The machine is big-endian.</exception>
    public static void RequireLittleEndian()
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException(
                "The .smodel format is little-endian only: its vertex, index and collision payloads are " +
                "reinterpreted in place, so a big-endian host would have to copy and byte-swap all of it.");
        }
    }
}
