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
        int recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(SentDef.HeaderSize + SentDef.TypeRecordSizeOffset));
        return SentDef.HeaderSize + recordSize - SentDef.TypeRecordTrailerSize;
    }

    // Takes bytes off the end of every type record, which is where a newer
    // build put its fields.
    public static byte[] WithShorterRecords(byte[] image, int bytes)
    {
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(SentDef.HeaderTypeCountOffset));
        int table = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(SentDef.HeaderStringTableOffsetOffset));

        var older = new List<byte>(image.AsSpan(0, SentDef.HeaderSize).ToArray());
        int at = SentDef.HeaderSize;
        for (int i = 0; i < count; i++)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at));
            int kept = size - bytes;

            Span<byte> record = image.AsSpan(at, kept).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(record, (uint)kept);
            older.AddRange(record);
            at += size;
        }

        older.AddRange(image.AsSpan(table));

        byte[] shorter = [.. older];
        BinaryPrimitives.WriteUInt32LittleEndian(
            shorter.AsSpan(SentDef.HeaderStringTableOffsetOffset), (uint)(table - (count * bytes)));
        return shorter;
    }
}
