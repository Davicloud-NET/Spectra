using System.Buffers.Binary;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

// Writes .smodel bytes from the format spec in docs/formats-and-pipeline.md.
// Uses no engine types or constants (FNV-1a included), so the reader and
// writer are checked against something other than themselves.
internal sealed class HandBuiltSmodel
{
    public const int HeaderSize = 64;
    public const int SectionTableOffset = 64;
    public const int SectionSize = 24;
    public const int PayloadAlignment = 16;
    public const uint NameOffsetAbsent = 0xFFFFFFFFu;

    // Overrides for building invalid files. A project that links this fixture
    // and only builds valid ones assigns none of them, hence CS0649.
#pragma warning disable CS0649
    public uint Magic = FourCc("SMDL");
    public ushort FormatVersion = 1;
    public ushort Flags;
    public uint GeometryFormatVersion = SpectraEngine.Core.EngineInfo.GeometryFormatVersion;
    public float[] Bounds = [-1f, -2f, -3f, 4f, 5f, 6f];

    public uint? VertexLayoutIdOverride;

    public uint? SectionCountOverride;
#pragma warning restore CS0649

    private readonly List<Entry> _sections = [];
    private uint _layoutId = FnvOffsetBasis;

    private const uint FnvOffsetBasis = 2166136261u;
    private const uint FnvPrime = 16777619u;

    private readonly record struct Entry(uint FourCc, byte[]? Payload, ulong Offset, ulong Length);

    public HandBuiltSmodel Section(string fourCc, byte[] payload)
    {
        _sections.Add(new Entry(FourCc(fourCc), payload, 0, 0));
        return this;
    }

    // A table record with no payload written: for sections past the file end
    // or off the alignment.
    public HandBuiltSmodel SectionAt(string fourCc, ulong offset, ulong length)
    {
        _sections.Add(new Entry(FourCc(fourCc), null, offset, length));
        return this;
    }

    // VTXL: u32 attributeCount, u32 strideFloats, then eight bytes per attribute.
    // Also records the layout id for the header.
    public HandBuiltSmodel VertexLayout(
        uint strideFloats,
        params (byte Semantic, byte ComponentType, byte ComponentCount, ushort ByteOffset)[] attributes)
    {
        var buffer = new Buf();
        buffer.U32((uint)attributes.Length);
        buffer.U32(strideFloats);

        uint hash = FnvOffsetBasis;
        foreach (var attribute in attributes)
        {
            buffer.U8(attribute.Semantic);
            buffer.U8(attribute.ComponentType);
            buffer.U8(attribute.ComponentCount);
            buffer.U8(0);                       // per-attribute flags
            buffer.U16(attribute.ByteOffset);
            buffer.U16(0);                      // reserved

            hash = (hash ^ attribute.Semantic) * FnvPrime;
            hash = (hash ^ attribute.ComponentCount) * FnvPrime;
        }

        _layoutId = hash;
        return Section("VTXL", buffer.ToArray());
    }

    // VBUF: interleaved floats.
    public HandBuiltSmodel VertexBuffer(params float[] floats)
    {
        var buffer = new Buf();
        foreach (float value in floats) buffer.F32(value);
        return Section("VBUF", buffer.ToArray());
    }

    // IBUF, 16-bit. The header's 32-bit flag must be clear.
    public HandBuiltSmodel Indices16(params ushort[] indices)
    {
        var buffer = new Buf();
        foreach (ushort value in indices) buffer.U16(value);
        return Section("IBUF", buffer.ToArray());
    }

    // IBUF, 32-bit. The header's 32-bit flag must be set.
    public HandBuiltSmodel Indices32(params uint[] indices)
    {
        var buffer = new Buf();
        foreach (uint value in indices) buffer.U32(value);
        return Section("IBUF", buffer.ToArray());
    }

    // SUBM: {u32 IndexStart, u32 IndexCount, u32 MaterialNameOffset, u32 Flags, f32[6] Bounds}.
    public HandBuiltSmodel Submeshes(params (uint Start, uint Count, uint MaterialName)[] submeshes)
    {
        var withBounds = new (uint, uint, uint, float[])[submeshes.Length];
        for (int i = 0; i < submeshes.Length; i++)
        {
            withBounds[i] = (
                submeshes[i].Start, submeshes[i].Count, submeshes[i].MaterialName,
                [-1f, -1f, -1f, 1f, 1f, 1f]);
        }

        return Submeshes(withBounds);
    }

    // Same record with explicit bounds, for comparing against a real writer's bytes.
    public HandBuiltSmodel Submeshes(
        params (uint Start, uint Count, uint MaterialName, float[] Bounds)[] submeshes)
    {
        var buffer = new Buf();
        foreach (var submesh in submeshes)
        {
            buffer.U32(submesh.Start);
            buffer.U32(submesh.Count);
            buffer.U32(submesh.MaterialName);
            buffer.U32(0);
            foreach (float value in submesh.Bounds) buffer.F32(value);
        }

        return Section("SUBM", buffer.ToArray());
    }

    // LODS: {f32 ScreenHeightThreshold, u32 FirstSubmesh, u32 SubmeshCount}.
    public HandBuiltSmodel Lods(params (float Threshold, uint FirstSubmesh, uint SubmeshCount)[] lods)
    {
        var buffer = new Buf();
        foreach (var lod in lods)
        {
            buffer.F32(lod.Threshold);
            buffer.U32(lod.FirstSubmesh);
            buffer.U32(lod.SubmeshCount);
        }

        return Section("LODS", buffer.ToArray());
    }

    // SKEL: {u32 NameOffset, i32 ParentIndex, f32[12] InverseBind}.
    public HandBuiltSmodel Skeleton(params (uint NameOffset, int Parent, float[] InverseBind)[] joints)
    {
        var buffer = new Buf();
        foreach (var joint in joints)
        {
            buffer.U32(joint.NameOffset);
            buffer.I32(joint.Parent);
            foreach (float value in joint.InverseBind) buffer.F32(value);
        }

        return Section("SKEL", buffer.ToArray());
    }

    // COLL: u32 hullCount, the hull table, padding to 16, then the flat plane array.
    public HandBuiltSmodel Collision(
        (uint PlaneStart, uint PlaneCount)[] hulls,
        (float Nx, float Ny, float Nz, float D)[] planes)
    {
        var buffer = new Buf();
        buffer.U32((uint)hulls.Length);
        foreach (var hull in hulls)
        {
            buffer.U32(hull.PlaneStart);
            buffer.U32(hull.PlaneCount);
        }

        buffer.AlignTo(PayloadAlignment);
        foreach (var plane in planes)
        {
            buffer.F32(plane.Nx);
            buffer.F32(plane.Ny);
            buffer.F32(plane.Nz);
            buffer.F32(plane.D);
        }

        return Section("COLL", buffer.ToArray());
    }

    // NAME: u16-prefixed UTF-8 records, back to back. Returns each record's offset.
    public HandBuiltSmodel Names(out uint[] offsets, params string[] names)
    {
        var buffer = new Buf();
        offsets = new uint[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            offsets[i] = (uint)buffer.Length;
            byte[] utf8 = Encoding.UTF8.GetBytes(names[i]);
            buffer.U16((ushort)utf8.Length);
            buffer.Bytes(utf8);
        }

        return Section("NAME", buffer.ToArray());
    }

    public byte[] Build()
    {
        int cursor = SectionTableOffset + (_sections.Count * SectionSize);
        var placed = new Entry[_sections.Count];
        for (int i = 0; i < _sections.Count; i++)
        {
            Entry entry = _sections[i];
            if (entry.Payload is null)
            {
                placed[i] = entry;
                continue;
            }

            cursor = AlignUp(cursor, PayloadAlignment);
            placed[i] = entry with { Offset = (ulong)cursor, Length = (ulong)entry.Payload.Length };
            cursor += entry.Payload.Length;
        }

        byte[] file = new byte[Math.Max(cursor, HeaderSize)];
        Span<byte> span = file;

        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x04..], FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x06..], Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x08..], GeometryFormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0C..], SectionCountOverride ?? (uint)_sections.Count);
        for (int i = 0; i < 6; i++)
            BinaryPrimitives.WriteSingleLittleEndian(span[(0x10 + (i * 4))..], Bounds[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x28..], VertexLayoutIdOverride ?? _layoutId);

        for (int i = 0; i < placed.Length; i++)
        {
            Span<byte> record = span.Slice(SectionTableOffset + (i * SectionSize), SectionSize);
            BinaryPrimitives.WriteUInt32LittleEndian(record, placed[i].FourCc);
            BinaryPrimitives.WriteUInt32LittleEndian(record[4..], 0);
            BinaryPrimitives.WriteUInt64LittleEndian(record[8..], placed[i].Offset);
            BinaryPrimitives.WriteUInt64LittleEndian(record[16..], placed[i].Length);

            placed[i].Payload?.CopyTo(span[(int)placed[i].Offset..]);
        }

        return file;
    }

    private static uint FourCc(string text) =>
        text[0] | ((uint)text[1] << 8) | ((uint)text[2] << 16) | ((uint)text[3] << 24);

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

    // Growable little-endian byte writer.
    private sealed class Buf
    {
        private readonly List<byte> _bytes = [];

        public int Length => _bytes.Count;

        public void U8(byte value) => _bytes.Add(value);

        public void U16(ushort value)
        {
            Span<byte> scratch = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(scratch, value);
            _bytes.AddRange(scratch);
        }

        public void U32(uint value)
        {
            Span<byte> scratch = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(scratch, value);
            _bytes.AddRange(scratch);
        }

        public void I32(int value)
        {
            Span<byte> scratch = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(scratch, value);
            _bytes.AddRange(scratch);
        }

        public void F32(float value)
        {
            Span<byte> scratch = stackalloc byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(scratch, value);
            _bytes.AddRange(scratch);
        }

        public void Bytes(ReadOnlySpan<byte> value) => _bytes.AddRange(value);

        public void AlignTo(int alignment)
        {
            while (_bytes.Count % alignment != 0) _bytes.Add(0);
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
