using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Writer, reader and layout constants for the <c>.sentdef</c> container, which
/// carries <see cref="EntitySchema"/>s out of the process. Little-endian, and
/// two writes of one catalogue give identical bytes.
/// </summary>
// Each type record starts with its own size, itself included. A reader advances
// by that size, so a newer writer can append fields without a version bump.
public static class SentDef
{
    /// <summary>
    /// The four bytes at offset 0, reading <c>SENT</c> in ASCII when written
    /// little-endian.
    /// </summary>
    public const uint Magic = 0x544E4553;

    /// <summary>
    /// The version this build writes, and the only one it reads. Bump it only
    /// for a change that moves a byte a reader already uses.
    /// </summary>
    public const ushort Version = 1;

    /// <summary>
    /// Bytes of header, which is also the offset of the first type record.
    /// Stored in the file so the header can grow without a version bump.
    /// </summary>
    public const ushort HeaderSize = 20;

    /// <summary>Offset of <see cref="Magic"/>.</summary>
    public const int HeaderMagicOffset = 0x00;

    /// <summary>Offset of the <c>u16</c> format version.</summary>
    public const int HeaderVersionOffset = 0x04;

    /// <summary>Offset of the <c>u16</c> header size.</summary>
    public const int HeaderSizeOffset = 0x06;

    /// <summary>Offset of the <c>u32</c> type count.</summary>
    public const int HeaderTypeCountOffset = 0x08;

    /// <summary>Offset of the <c>u32</c> absolute string-table offset.</summary>
    public const int HeaderStringTableOffsetOffset = 0x0C;

    /// <summary>Offset of the <c>u32</c> string-table size in bytes.</summary>
    public const int HeaderStringTableSizeOffset = 0x10;

    /// <summary>Bytes of type record before the first keyvalue record.</summary>
    public const int TypeRecordFixedSize = 0x18;

    /// <summary>Offset, within a type record, of its self-including size.</summary>
    public const int TypeRecordSizeOffset = 0x00;

    /// <summary>Offset, within a type record, of the class-name string reference.</summary>
    public const int TypeRecordClassNameOffset = 0x04;

    /// <summary>Offset, within a type record, of the display-name string reference.</summary>
    public const int TypeRecordDisplayNameOffset = 0x08;

    /// <summary>Offset, within a type record, of the group string reference.</summary>
    public const int TypeRecordGroupOffset = 0x0C;

    /// <summary>Offset, within a type record, of the <see cref="EntityPlacement"/> byte.</summary>
    public const int TypeRecordPlacementOffset = 0x10;

    /// <summary>
    /// Offset, within a type record, of the <see cref="EntityOrigin"/> byte: the
    /// one byte two producers of the same class may differ in.
    /// </summary>
    public const int TypeRecordOriginOffset = 0x11;

    /// <summary>Offset, within a type record, of the <c>u16</c> keyvalue count.</summary>
    public const int TypeRecordKeyvalueCountOffset = 0x12;

    /// <summary>Offset, within a type record, of the <c>u16</c> input count.</summary>
    public const int TypeRecordInputCountOffset = 0x14;

    /// <summary>Offset, within a type record, of the <c>u16</c> output count.</summary>
    public const int TypeRecordOutputCountOffset = 0x16;

    /// <summary>
    /// Bytes of keyvalue record, before its choice list. Fixed: a new field
    /// here breaks every file already written, so new data goes into the
    /// <see cref="KeyvalueDescriptor.Flags"/> word.
    /// </summary>
    public const int KeyvalueRecordSize = 0x20;

    /// <summary>Bytes of one choice record: two string references.</summary>
    public const int ChoiceRecordSize = 8;

    /// <summary>Bytes of one string reference, and of one input or output record.</summary>
    public const int StringRefSize = 4;

    // Throws on invalid text. A U+FFFD replacement would change a class name
    // and the map naming it would load a placeholder with no error.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Writes <paramref name="schemas"/> as a <c>.sentdef</c> image, sorted by
    /// class name. The caller's list is not reordered.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Two schemas claim one class name, a count or a string exceeds what the
    /// layout can express, a placement or origin is outside its enum, or a
    /// keyvalue sets a reserved flag bit.
    /// </exception>
    public static byte[] Write(IReadOnlyList<EntitySchema> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        EntitySchema[] ordered = SortedByClassName(schemas);

        // Two passes: measure and intern, then write into an array of that size.
        var strings = new StringTableBuilder();
        var recordSizes = new int[ordered.Length];
        long recordBytes = 0;
        for (int i = 0; i < ordered.Length; i++)
        {
            recordSizes[i] = MeasureAndIntern(ordered[i], strings);
            recordBytes += recordSizes[i];
        }

        long stringTableOffset = HeaderSize + recordBytes;
        long total = stringTableOffset + strings.Length;
        if (total > int.MaxValue)
        {
            throw new ArgumentException(
                $"A .sentdef image would be {total} bytes, which its u32 offsets cannot address.",
                nameof(schemas));
        }

        var bytes = new byte[(int)total];
        Span<byte> image = bytes;

        BinaryPrimitives.WriteUInt32LittleEndian(image[HeaderMagicOffset..], Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(image[HeaderVersionOffset..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(image[HeaderSizeOffset..], HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(image[HeaderTypeCountOffset..], (uint)ordered.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(image[HeaderStringTableOffsetOffset..], (uint)stringTableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(image[HeaderStringTableSizeOffset..], (uint)strings.Length);

        int position = HeaderSize;
        for (int i = 0; i < ordered.Length; i++)
        {
            WriteType(image.Slice(position, recordSizes[i]), ordered[i], strings);
            position += recordSizes[i];
        }

        // A string interned only by the write pass would overrun the image.
        if (strings.Length != (int)(total - stringTableOffset))
        {
            throw new InvalidOperationException(
                "The .sentdef write pass interned a string the measure pass did not; the two passes disagree " +
                "about the layout.");
        }

        strings.CopyTo(image[position..]);
        return bytes;
    }

    /// <summary>
    /// Reads a <c>.sentdef</c> image back into schemas, in the file's order.
    /// Nothing returned points into <paramref name="image"/>, so a mapped view
    /// can be released afterwards.
    /// </summary>
    /// <exception cref="SentDefFormatException">The image is not one this build can read.</exception>
    public static EntitySchema[] Read(ReadOnlySpan<byte> image)
    {
        if (image.Length < HeaderSize)
        {
            throw new SentDefFormatException(
                $"Truncated: a .sentdef header is {HeaderSize} bytes and this image is {image.Length}.", 0);
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(image[HeaderMagicOffset..]);
        if (magic != Magic)
        {
            throw new SentDefFormatException(
                $"Not a .sentdef: expected the magic 'SENT' (0x{Magic:X8}) and found 0x{magic:X8}.",
                HeaderMagicOffset);
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(image[HeaderVersionOffset..]);
        if (version != Version)
        {
            throw new SentDefFormatException(
                $"This build reads .sentdef version {Version} and the image declares version {version}. " +
                "Re-export the entity schemas with a matching engine.",
                HeaderVersionOffset);
        }

        ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(image[HeaderSizeOffset..]);
        if (headerSize < HeaderSize || headerSize > image.Length)
        {
            throw new SentDefFormatException(
                $"HeaderSize is {headerSize}, which is outside the {HeaderSize}..{image.Length} a readable image allows.",
                HeaderSizeOffset);
        }

        uint typeCount = BinaryPrimitives.ReadUInt32LittleEndian(image[HeaderTypeCountOffset..]);
        uint stringTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(image[HeaderStringTableOffsetOffset..]);
        uint stringTableSize = BinaryPrimitives.ReadUInt32LittleEndian(image[HeaderStringTableSizeOffset..]);

        if (stringTableOffset < headerSize ||
            stringTableOffset > (uint)image.Length ||
            stringTableSize > (uint)image.Length - stringTableOffset)
        {
            throw new SentDefFormatException(
                $"The string table claims {stringTableSize} bytes at offset {stringTableOffset}, " +
                $"which does not fit inside a {image.Length}-byte image.",
                HeaderStringTableOffsetOffset);
        }

        ReadOnlySpan<byte> table = image.Slice((int)stringTableOffset, (int)stringTableSize);

        // Reference 0 is the empty string and is never looked up, so the table
        // has to really start with one.
        if (table.Length < sizeof(ushort) || BinaryPrimitives.ReadUInt16LittleEndian(table) != 0)
        {
            throw new SentDefFormatException(
                "The string table must open with a zero-length record, which is the empty string every " +
                "reference of 0 resolves to.",
                (long)stringTableOffset);
        }

        // Before the allocation: a corrupt count would otherwise be an OutOfMemoryException.
        if ((long)typeCount * TypeRecordFixedSize > image.Length - headerSize)
        {
            throw new SentDefFormatException(
                $"The header claims {typeCount} type(s), which cannot fit in the " +
                $"{image.Length - headerSize} bytes after the header.",
                HeaderTypeCountOffset);
        }

        var schemas = new EntitySchema[typeCount];
        var decoded = new Dictionary<uint, string>();
        int cursor = headerSize;
        string previousClassName = "";

        for (int i = 0; i < schemas.Length; i++)
        {
            if (image.Length - cursor < TypeRecordFixedSize)
            {
                throw new SentDefFormatException(
                    $"Truncated: type record {i} needs {TypeRecordFixedSize} bytes and " +
                    $"{image.Length - cursor} remain.",
                    cursor);
            }

            uint recordSize = BinaryPrimitives.ReadUInt32LittleEndian(image[(cursor + TypeRecordSizeOffset)..]);
            if (recordSize < TypeRecordFixedSize)
            {
                throw new SentDefFormatException(
                    $"Type record {i} declares a size of {recordSize}, below the {TypeRecordFixedSize} " +
                    "bytes every record starts with.",
                    cursor);
            }

            if (recordSize > (uint)(image.Length - cursor))
            {
                throw new SentDefFormatException(
                    $"Truncated: type record {i} declares {recordSize} bytes and " +
                    $"{image.Length - cursor} remain.",
                    cursor);
            }

            EntitySchema schema = ReadType(image.Slice(cursor, (int)recordSize), table, decoded, i, cursor);

            // Sorted order is part of the format. Strictly increasing also
            // rejects a duplicate class name.
            if (i > 0 && string.CompareOrdinal(previousClassName, schema.ClassName) >= 0)
            {
                throw new SentDefFormatException(
                    $"Type records must be sorted by class name (ordinal) and strictly unique: " +
                    $"'{schema.ClassName}' follows '{previousClassName}'.",
                    cursor + TypeRecordClassNameOffset);
            }

            previousClassName = schema.ClassName;
            schemas[i] = schema;

            // By the declared size, not by what was parsed: skips fields a
            // newer writer appended.
            cursor += (int)recordSize;
        }

        return schemas;
    }

    // Ordinal: a culture sort would give a different file per machine.
    private static EntitySchema[] SortedByClassName(IReadOnlyList<EntitySchema> schemas)
    {
        var ordered = new EntitySchema[schemas.Count];
        for (int i = 0; i < ordered.Length; i++)
        {
            ordered[i] = schemas[i] ?? throw new ArgumentException(
                $"Schema {i} is null; a .sentdef entry has to describe a class.", nameof(schemas));
        }

        Array.Sort(ordered, static (a, b) => string.CompareOrdinal(a.ClassName, b.ClassName));

        for (int i = 1; i < ordered.Length; i++)
        {
            if (string.Equals(ordered[i - 1].ClassName, ordered[i].ClassName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Two schemas claim the class name '{ordered[i].ClassName}'. One name is one class, " +
                    "or a map means different things depending on which record a reader kept.",
                    nameof(schemas));
            }
        }

        return ordered;
    }

    private static int MeasureAndIntern(EntitySchema schema, StringTableBuilder strings)
    {
        if (!Enum.IsDefined(schema.Placement))
        {
            throw new ArgumentException(
                $"Class '{schema.ClassName}' declares placement {(byte)schema.Placement}, which is not an " +
                $"{nameof(EntityPlacement)} this build names.", nameof(schema));
        }

        if (!Enum.IsDefined(schema.Origin))
        {
            throw new ArgumentException(
                $"Class '{schema.ClassName}' declares origin {(byte)schema.Origin}, which is not an " +
                $"{nameof(EntityOrigin)} this build names.", nameof(schema));
        }

        // Interning order is the string table's layout. Keep it in the
        // record's field order or the output stops being deterministic.
        strings.Intern(schema.ClassName);
        strings.Intern(schema.DisplayName);
        strings.Intern(schema.Group);

        RequireUInt16(schema.Keyvalues.Count, "keyvalues", schema.ClassName);
        RequireUInt16(schema.Inputs.Count, "inputs", schema.ClassName);
        RequireUInt16(schema.Outputs.Count, "outputs", schema.ClassName);

        int size = TypeRecordFixedSize;
        for (int i = 0; i < schema.Keyvalues.Count; i++)
        {
            KeyvalueDescriptor keyvalue = schema.Keyvalues[i];

            if (!Enum.IsDefined(keyvalue.Type))
            {
                throw new ArgumentException(
                    $"Keyvalue '{keyvalue.Name}' on class '{schema.ClassName}' declares type " +
                    $"{(byte)keyvalue.Type}, which is not a {nameof(KeyvalueType)} this build names.",
                    nameof(schema));
            }

            // Reserved bits throw here and are masked on read. Dropping one on
            // write would lose data the producer meant to ship.
            uint reserved = keyvalue.Flags & ~KeyvalueFlags.DefinedMask;
            if (reserved != 0)
            {
                throw new ArgumentException(
                    $"Keyvalue '{keyvalue.Name}' on class '{schema.ClassName}' sets reserved flag bits " +
                    $"0x{reserved:X8}. Those bits are claimed and unassigned; widen " +
                    $"{nameof(KeyvalueFlags)}.{nameof(KeyvalueFlags.DefinedMask)} in the change that gives " +
                    "them meaning.", nameof(schema));
            }

            strings.Intern(keyvalue.Name);
            strings.Intern(keyvalue.Display);
            strings.Intern(keyvalue.Tooltip);
            strings.Intern(keyvalue.Default);

            IReadOnlyList<(string Value, string Display)> choices = keyvalue.Choices ?? KeyvalueDescriptor.NoChoices;
            RequireUInt16(choices.Count, $"choices on keyvalue '{keyvalue.Name}'", schema.ClassName);
            for (int c = 0; c < choices.Count; c++)
            {
                strings.Intern(choices[c].Value);
                strings.Intern(choices[c].Display);
            }

            size += KeyvalueRecordSize + (choices.Count * ChoiceRecordSize);
        }

        for (int i = 0; i < schema.Inputs.Count; i++)
            strings.Intern(schema.Inputs[i]);
        for (int i = 0; i < schema.Outputs.Count; i++)
            strings.Intern(schema.Outputs[i]);

        return size + ((schema.Inputs.Count + schema.Outputs.Count) * StringRefSize);
    }

    private static void WriteType(Span<byte> record, EntitySchema schema, StringTableBuilder strings)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(record[TypeRecordSizeOffset..], (uint)record.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(record[TypeRecordClassNameOffset..], strings.Intern(schema.ClassName));
        BinaryPrimitives.WriteUInt32LittleEndian(record[TypeRecordDisplayNameOffset..], strings.Intern(schema.DisplayName));
        BinaryPrimitives.WriteUInt32LittleEndian(record[TypeRecordGroupOffset..], strings.Intern(schema.Group));
        record[TypeRecordPlacementOffset] = (byte)schema.Placement;
        record[TypeRecordOriginOffset] = (byte)schema.Origin;
        BinaryPrimitives.WriteUInt16LittleEndian(record[TypeRecordKeyvalueCountOffset..], (ushort)schema.Keyvalues.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(record[TypeRecordInputCountOffset..], (ushort)schema.Inputs.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(record[TypeRecordOutputCountOffset..], (ushort)schema.Outputs.Count);

        int position = TypeRecordFixedSize;
        for (int i = 0; i < schema.Keyvalues.Count; i++)
        {
            KeyvalueDescriptor keyvalue = schema.Keyvalues[i];
            IReadOnlyList<(string Value, string Display)> choices = keyvalue.Choices ?? KeyvalueDescriptor.NoChoices;
            Span<byte> field = record[position..];

            BinaryPrimitives.WriteUInt32LittleEndian(field[0x00..], strings.Intern(keyvalue.Name));
            BinaryPrimitives.WriteUInt32LittleEndian(field[0x04..], strings.Intern(keyvalue.Display));
            BinaryPrimitives.WriteUInt32LittleEndian(field[0x08..], strings.Intern(keyvalue.Tooltip));
            BinaryPrimitives.WriteUInt32LittleEndian(field[0x0C..], strings.Intern(keyvalue.Default));
            field[0x10] = (byte)keyvalue.Type;

            // Not validated: an unknown widget reads back as Auto here, and a
            // newer editor that knows it still gets the byte.
            field[0x11] = keyvalue.Widget;
            BinaryPrimitives.WriteUInt16LittleEndian(field[0x12..], (ushort)choices.Count);

            // NaN means unbounded. Written as raw bits.
            BinaryPrimitives.WriteSingleLittleEndian(field[0x14..], keyvalue.Min);
            BinaryPrimitives.WriteSingleLittleEndian(field[0x18..], keyvalue.Max);
            BinaryPrimitives.WriteUInt32LittleEndian(field[0x1C..], keyvalue.Flags);

            position += KeyvalueRecordSize;
            for (int c = 0; c < choices.Count; c++)
            {
                Span<byte> choice = record[position..];
                BinaryPrimitives.WriteUInt32LittleEndian(choice[0..], strings.Intern(choices[c].Value));
                BinaryPrimitives.WriteUInt32LittleEndian(choice[4..], strings.Intern(choices[c].Display));
                position += ChoiceRecordSize;
            }
        }

        for (int i = 0; i < schema.Inputs.Count; i++, position += StringRefSize)
            BinaryPrimitives.WriteUInt32LittleEndian(record[position..], strings.Intern(schema.Inputs[i]));

        for (int i = 0; i < schema.Outputs.Count; i++, position += StringRefSize)
            BinaryPrimitives.WriteUInt32LittleEndian(record[position..], strings.Intern(schema.Outputs[i]));

        if (position != record.Length)
        {
            // MeasureAndIntern and WriteType disagree about the layout.
            throw new InvalidOperationException(
                $"Wrote {position} bytes into a {record.Length}-byte record for '{schema.ClassName}'.");
        }
    }

    private static EntitySchema ReadType(
        ReadOnlySpan<byte> record,
        ReadOnlySpan<byte> table,
        Dictionary<uint, string> decoded,
        int index,
        long recordOffset)
    {
        string className = ReadString(record, table, decoded, TypeRecordClassNameOffset, recordOffset, index);
        if (className.Length == 0)
        {
            throw new SentDefFormatException(
                $"Type record {index} has an empty class name; a class with no name cannot be looked up.",
                recordOffset + TypeRecordClassNameOffset);
        }

        string displayName = ReadString(record, table, decoded, TypeRecordDisplayNameOffset, recordOffset, index);
        string group = ReadString(record, table, decoded, TypeRecordGroupOffset, recordOffset, index);

        byte placement = record[TypeRecordPlacementOffset];
        if (!Enum.IsDefined((EntityPlacement)placement))
        {
            // No fallback: placement decides whether a node carries brush geometry.
            throw new SentDefFormatException(
                $"Class '{className}' declares placement {placement}, which is not an " +
                $"{nameof(EntityPlacement)} this build names.",
                recordOffset + TypeRecordPlacementOffset);
        }

        byte origin = record[TypeRecordOriginOffset];
        if (!Enum.IsDefined((EntityOrigin)origin))
        {
            throw new SentDefFormatException(
                $"Class '{className}' declares origin {origin}, which is not an " +
                $"{nameof(EntityOrigin)} this build names.",
                recordOffset + TypeRecordOriginOffset);
        }

        int keyvalueCount = BinaryPrimitives.ReadUInt16LittleEndian(record[TypeRecordKeyvalueCountOffset..]);
        int inputCount = BinaryPrimitives.ReadUInt16LittleEndian(record[TypeRecordInputCountOffset..]);
        int outputCount = BinaryPrimitives.ReadUInt16LittleEndian(record[TypeRecordOutputCountOffset..]);

        var keyvalues = new KeyvalueDescriptor[keyvalueCount];
        int position = TypeRecordFixedSize;

        for (int i = 0; i < keyvalueCount; i++)
        {
            RequireRoom(record.Length, position, KeyvalueRecordSize, className, "keyvalue record", recordOffset);
            ReadOnlySpan<byte> field = record.Slice(position, KeyvalueRecordSize);

            string name = ReadString(field, table, decoded, 0x00, recordOffset + position, index);
            string display = ReadString(field, table, decoded, 0x04, recordOffset + position, index);
            string tooltip = ReadString(field, table, decoded, 0x08, recordOffset + position, index);
            string @default = ReadString(field, table, decoded, 0x0C, recordOffset + position, index);

            byte type = field[0x10];
            if (!Enum.IsDefined((KeyvalueType)type))
            {
                throw new SentDefFormatException(
                    $"Keyvalue '{name}' on class '{className}' declares type {type}, which is not a " +
                    $"{nameof(KeyvalueType)} this build names.",
                    recordOffset + position + 0x10);
            }

            // Unknown widget falls back to Auto rather than failing the file.
            byte widget = field[0x11];
            if (!KeyvalueWidget.IsDefined(widget))
                widget = KeyvalueWidget.Auto;

            int choiceCount = BinaryPrimitives.ReadUInt16LittleEndian(field[0x12..]);
            float min = BinaryPrimitives.ReadSingleLittleEndian(field[0x14..]);
            float max = BinaryPrimitives.ReadSingleLittleEndian(field[0x18..]);

            // Drop bits this build does not know; another tool may have set them.
            uint flags = KeyvalueFlags.Mask(BinaryPrimitives.ReadUInt32LittleEndian(field[0x1C..]));

            position += KeyvalueRecordSize;

            IReadOnlyList<(string Value, string Display)> choices;
            if (choiceCount == 0)
            {
                choices = KeyvalueDescriptor.NoChoices;
            }
            else
            {
                var list = new (string Value, string Display)[choiceCount];
                for (int c = 0; c < choiceCount; c++)
                {
                    RequireRoom(record.Length, position, ChoiceRecordSize, className, "choice record", recordOffset);
                    ReadOnlySpan<byte> choice = record.Slice(position, ChoiceRecordSize);
                    list[c] = (
                        ReadString(choice, table, decoded, 0, recordOffset + position, index),
                        ReadString(choice, table, decoded, 4, recordOffset + position, index));
                    position += ChoiceRecordSize;
                }

                choices = list;
            }

            keyvalues[i] = new KeyvalueDescriptor(
                name, display, tooltip, @default, (KeyvalueType)type, widget, min, max, flags, choices);
        }

        var inputs = new string[inputCount];
        for (int i = 0; i < inputCount; i++, position += StringRefSize)
        {
            RequireRoom(record.Length, position, StringRefSize, className, "input record", recordOffset);
            inputs[i] = ReadString(record, table, decoded, position, recordOffset, index);
        }

        var outputs = new string[outputCount];
        for (int i = 0; i < outputCount; i++, position += StringRefSize)
        {
            RequireRoom(record.Length, position, StringRefSize, className, "output record", recordOffset);
            outputs[i] = ReadString(record, table, decoded, position, recordOffset, index);
        }

        // Bytes left over are a newer writer's fields. Ignored.
        return new EntitySchema(
            className, displayName, group, (EntityPlacement)placement, (EntityOrigin)origin,
            keyvalues, inputs, outputs);
    }

    private static void RequireRoom(
        int recordLength, int position, int needed, string className, string what, long recordOffset)
    {
        if (recordLength - position >= needed)
            return;

        throw new SentDefFormatException(
            $"Class '{className}' declares more content than its record holds: a {what} needs {needed} " +
            $"bytes at offset {position} of a {recordLength}-byte record.",
            recordOffset + position);
    }

    // Cached by table offset, so a repeated string is one object in memory.
    private static string ReadString(
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> table,
        Dictionary<uint, string> decoded,
        int fieldOffset,
        long absoluteFieldOffset,
        int typeIndex)
    {
        uint reference = BinaryPrimitives.ReadUInt32LittleEndian(source[fieldOffset..]);
        if (reference == 0)
            return string.Empty;

        if (decoded.TryGetValue(reference, out string? cached))
            return cached;

        if (reference > (uint)table.Length - sizeof(ushort))
        {
            throw new SentDefFormatException(
                $"Type record {typeIndex} references string offset {reference}, which is outside the " +
                $"{table.Length}-byte string table.",
                absoluteFieldOffset + fieldOffset);
        }

        ReadOnlySpan<byte> at = table[(int)reference..];
        int length = BinaryPrimitives.ReadUInt16LittleEndian(at);
        if (length > at.Length - sizeof(ushort))
        {
            throw new SentDefFormatException(
                $"The string at offset {reference} claims {length} bytes and the table has " +
                $"{at.Length - sizeof(ushort)} left.",
                absoluteFieldOffset + fieldOffset);
        }

        string text;
        try
        {
            text = Utf8.GetString(at.Slice(sizeof(ushort), length));
        }
        catch (DecoderFallbackException ex)
        {
            throw new SentDefFormatException(
                $"The string at offset {reference} is not valid UTF-8.", absoluteFieldOffset + fieldOffset, ex);
        }

        decoded.Add(reference, text);
        return text;
    }

    private static void RequireUInt16(int count, string what, string className)
    {
        if (count is >= 0 and <= ushort.MaxValue)
            return;

        throw new ArgumentException(
            $"Class '{className}' declares {count} {what}; the record stores that count as a u16.");
    }

    // Deduplicated string table. Ordinal keys, empty string at offset 0.
    private sealed class StringTableBuilder
    {
        private readonly Dictionary<string, uint> _offsets = new(StringComparer.Ordinal);
        private readonly List<byte> _bytes = [];

        public StringTableBuilder() => Intern(string.Empty);

        public int Length => _bytes.Count;

        public uint Intern(string? value)
        {
            string text = value ?? string.Empty;
            if (_offsets.TryGetValue(text, out uint existing))
                return existing;

            int byteCount = Utf8.GetByteCount(text);
            if (byteCount > ushort.MaxValue)
            {
                throw new ArgumentException(
                    $"A .sentdef string is length-prefixed with a u16 and this one is {byteCount} bytes: " +
                    $"'{text[..Math.Min(text.Length, 40)]}...'.");
            }

            uint offset = (uint)_bytes.Count;
            Span<byte> prefix = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16LittleEndian(prefix, (ushort)byteCount);
            _bytes.Add(prefix[0]);
            _bytes.Add(prefix[1]);
            if (byteCount > 0)
                _bytes.AddRange(Utf8.GetBytes(text));

            _offsets.Add(text, offset);
            return offset;
        }

        public void CopyTo(Span<byte> destination) => CollectionsMarshal.AsSpan(_bytes).CopyTo(destination);
    }
}
