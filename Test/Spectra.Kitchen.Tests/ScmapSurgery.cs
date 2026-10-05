using System;
using System.Buffers.Binary;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Tests;

// Finds a section of a compiled map from the file's own table, so a test can
// read or damage bytes at an offset the spec names.
internal static class ScmapSurgery
{
    // Offset of a section's 32-byte record in the section table.
    public static int TableRecord(byte[] file, uint kind)
    {
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x0C));
        for (int i = 0; i < count; i++)
        {
            int record = ScmapFormat.SectionTableOffset + (i * ScmapFormat.SectionSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(record)) == kind) return record;
        }

        throw new InvalidOperationException($"No '{ScmapFormat.DescribeFourCc(kind)}' section in the file.");
    }

    public static (int Offset, int Size) Section(byte[] file, uint kind)
    {
        int record = TableRecord(file, kind);
        return (
            (int)BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(record + 0x08)),
            (int)BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(record + 0x10)));
    }

    public static byte[] Body(byte[] file, uint kind)
    {
        (int offset, int size) = Section(file, kind);
        return file.AsSpan(offset, size).ToArray();
    }

    // Stored and decoded size both: the reader refuses a file where they differ.
    public static void Resize(byte[] file, uint kind, ulong size)
    {
        int record = TableRecord(file, kind);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(record + 0x10), size);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(record + 0x18), size);
    }

    public static uint U32(byte[] file, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset));

    public static void SetU32(byte[] file, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), value);
}
