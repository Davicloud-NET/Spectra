using SpectraEngine.Core.Entities;
using System;
using System.Buffers.Binary;

namespace SpectraEngine.Entities.Tests;

// Lays a .sentdef image out the way an older build wrote it.
internal static class SentDefImages
{
    // Where the first type record's trailer starts, in an image this build wrote.
    public static int FirstTrailer(byte[] image)
    {
        int record = SentDef.HeaderSize;
        int recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(record + SentDef.TypeRecordSizeOffset));
        return record + recordSize - ParameterBytes(image, record) - SentDef.TrailerFixedSize;
    }

    // Where the first type record's parameter types start.
    public static int FirstParameters(byte[] image) => FirstTrailer(image) + SentDef.TrailerParametersOffset;

    // What the build before the parameter types wrote: every record ends at
    // its class flags.
    public static byte[] WithoutParameterTypes(byte[] image) => WithShorterRecords(image, 0);

    // Takes the parameter types off the end of every type record and then
    // this many bytes more, which is where a newer build put its fields.
    public static byte[] WithShorterRecords(byte[] image, int bytes)
    {
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(SentDef.HeaderTypeCountOffset));
        int table = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(SentDef.HeaderStringTableOffsetOffset));

        var older = new List<byte>(image.AsSpan(0, SentDef.HeaderSize).ToArray());
        int at = SentDef.HeaderSize;
        int removed = 0;
        for (int i = 0; i < count; i++)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at));
            int kept = size - ParameterBytes(image, at) - bytes;

            Span<byte> record = image.AsSpan(at, kept).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(record, (uint)kept);
            older.AddRange(record);
            at += size;
            removed += size - kept;
        }

        older.AddRange(image.AsSpan(table));

        byte[] shorter = [.. older];
        BinaryPrimitives.WriteUInt32LittleEndian(
            shorter.AsSpan(SentDef.HeaderStringTableOffsetOffset), (uint)(table - removed));
        return shorter;
    }

    // The bytes the record at this offset spends on its parameter types.
    private static int ParameterBytes(byte[] image, int record) => SentDef.ParameterBytes(
        BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(record + SentDef.TypeRecordInputCountOffset)),
        BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(record + SentDef.TypeRecordOutputCountOffset)));
}
