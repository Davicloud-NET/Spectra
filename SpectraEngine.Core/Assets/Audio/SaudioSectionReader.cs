using SpectraEngine.Core.Assets.Models;
using SpectraEngine.Core.Audio;
using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;

namespace SpectraEngine.Core.Assets.Audio;

// Reads a .saudio's section table and the sections this build knows. A tag it
// does not know is skipped, the way the .scmap reader skips one.
internal static class SaudioSectionReader
{
    // Returns the file's markers, or none when it has no marker section.
    // tableOffset is the header field, where 0 means no table. skipped counts
    // the sections left unread.
    public static AudioMarker[] Read(
        ReadOnlySpan<byte> file,
        uint tableOffset,
        long frameCount,
        string origin,
        out int skipped)
    {
        skipped = 0;
        if (tableOffset == 0) return [];

        uint count = RequireTable(file, tableOffset, origin);
        int entriesAt = (int)tableOffset + SaudioFormat.SectionTableHeaderSize;

        AudioMarker[]? markers = null;

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = file.Slice(
                entriesAt + (i * SaudioFormat.SectionEntrySize), SaudioFormat.SectionEntrySize);

            // Sliced for unknown sections too, so the skip rule cannot let a
            // malformed file through.
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            ReadOnlySpan<byte> section = RequireSection(file, entry, origin);

            if (tag != SaudioFormat.MarkerSection)
            {
                skipped++;
                continue;
            }

            if (markers is not null)
            {
                throw SaudioReader.Refuse(
                    origin,
                    "it carries the 'MARK' section more than once, and a reader would have to choose one.");
            }

            markers = ReadMarkers(section, frameCount, origin);
        }

        return markers ?? [];
    }

    // Returns the number of entries, once the whole table is known to lie in the file.
    private static uint RequireTable(ReadOnlySpan<byte> file, uint tableOffset, string origin)
    {
        if (tableOffset < SaudioFormat.HeaderSize ||
            (long)tableOffset + SaudioFormat.SectionTableHeaderSize > file.Length)
        {
            throw SaudioReader.Refuse(
                origin,
                $"its section table starts at byte {tableOffset}, which is outside the {file.Length}-byte file.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(file[(int)tableOffset..]);
        long tableEnd = (long)tableOffset + SaudioFormat.SectionTableHeaderSize +
            ((long)count * SaudioFormat.SectionEntrySize);

        if (tableEnd > file.Length)
        {
            throw SaudioReader.Refuse(
                origin,
                $"its section table declares {count} sections, whose {SaudioFormat.SectionEntrySize}-byte " +
                $"entries would end at byte {tableEnd} of a {file.Length}-byte file.");
        }

        return count;
    }

    // The bytes a table entry names.
    private static ReadOnlySpan<byte> RequireSection(ReadOnlySpan<byte> file, ReadOnlySpan<byte> entry, string origin)
    {
        uint tag = BinaryPrimitives.ReadUInt32LittleEndian(entry);
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);

        // Subtract, don't add: the sum can wrap.
        if (offset < SaudioFormat.HeaderSize || offset > (uint)file.Length || length > (uint)file.Length - offset)
        {
            throw SaudioReader.Refuse(
                origin,
                $"its '{SmodelFormat.DescribeFourCc(tag)}' section claims {length} bytes at offset {offset}, " +
                $"which is not inside the {file.Length}-byte file after the header.");
        }

        return file.Slice((int)offset, (int)length);
    }

    private static AudioMarker[] ReadMarkers(ReadOnlySpan<byte> section, long frameCount, string origin)
    {
        if (section.Length < SaudioFormat.MarkerCountSize)
        {
            throw SaudioReader.Refuse(
                origin,
                $"its marker section is {section.Length} bytes, too short to hold its own count.");
        }

        // Checked before allocating: a garbage count must not ask for gigabytes.
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(section);
        if ((long)count * SaudioFormat.MarkerRecordHeaderSize > section.Length - SaudioFormat.MarkerCountSize)
        {
            throw SaudioReader.Refuse(
                origin,
                $"its marker section declares {count} markers and is {section.Length} bytes, which cannot " +
                "hold that many.");
        }

        var markers = new AudioMarker[count];
        int at = SaudioFormat.MarkerCountSize;
        long previous = 0;

        for (int i = 0; i < markers.Length; i++)
        {
            if (section.Length - at < SaudioFormat.MarkerRecordHeaderSize)
            {
                throw SaudioReader.Refuse(
                    origin,
                    $"its marker {i} starts at byte {at} of a {section.Length}-byte marker section, which ends " +
                    "before the record does.");
            }

            ulong frame = BinaryPrimitives.ReadUInt64LittleEndian(section[at..]);
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(section[(at + 8)..]);
            at += SaudioFormat.MarkerRecordHeaderSize;

            if (section.Length - at < nameLength)
            {
                throw SaudioReader.Refuse(
                    origin,
                    $"its marker {i} has a {nameLength}-byte name and {section.Length - at} bytes are left in " +
                    "the marker section.");
            }

            ReadOnlySpan<byte> name = section.Slice(at, nameLength);
            at += nameLength;

            previous = RequireFrame(frame, i, previous, frameCount, origin);
            markers[i] = new AudioMarker(previous, RequireName(name, i, origin));
        }

        return markers;
    }

    // Frame order is what lets a player walk the markers with one cursor.
    private static long RequireFrame(ulong frame, int index, long previous, long frameCount, string origin)
    {
        if (frame > (ulong)frameCount)
        {
            throw SaudioReader.Refuse(
                origin,
                $"its marker {index} is at frame {frame} and the sound is {frameCount} frames long.");
        }

        if ((long)frame < previous)
        {
            throw SaudioReader.Refuse(
                origin,
                $"its marker {index} is at frame {frame} and marker {index - 1} is at {previous}; markers are " +
                "stored in the order a playing sound reaches them.");
        }

        return (long)frame;
    }

    private static string RequireName(ReadOnlySpan<byte> name, int index, string origin)
    {
        if (name.IsEmpty)
            throw SaudioReader.Refuse(origin, $"its marker {index} has no name, so nothing could react to it.");

        if (!Utf8.IsValid(name))
            throw SaudioReader.Refuse(origin, $"its marker {index} has a name that is not UTF-8 text.");

        return Encoding.UTF8.GetString(name);
    }
}
