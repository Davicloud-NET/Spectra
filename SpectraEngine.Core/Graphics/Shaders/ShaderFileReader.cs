using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpectraEngine.Core.Graphics.Shaders;

/// <summary>
/// Reads a .specshadecomp file, whole or one backend's blob.
/// </summary>
// Two parsers over one layout: a stream one, and a span one for mapped pack
// views (a MemoryStream would copy the whole file). The span parser throws on a
// truncated file; BinaryReader.ReadBytes returns a short array instead.
public static class ShaderFileReader
{
    /// <summary>Reads the full compiled shader file from a stream.</summary>
    public static CompiledShaderFile Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        Span<byte> magic = stackalloc byte[4];
        if (reader.Read(magic) != 4 || !magic.SequenceEqual(CompiledShaderFile.MagicBytes))
            throw new InvalidDataException("Not a valid .specshadecomp file (bad magic bytes)");

        ushort formatVersion = RequireSupportedVersion(reader.ReadUInt16());
        var stages = (ShaderStageFlags)reader.ReadByte();
        byte pipelineCount = reader.ReadByte();

        var entries = new ShaderPipelineEntry[pipelineCount];
        for (int i = 0; i < pipelineCount; i++)
        {
            var backend = (GraphicsBackend)reader.ReadByte();
            var format = (ShaderDataFormat)reader.ReadByte();
            var entryStages = (ShaderStageFlags)reader.ReadByte();
            reader.ReadByte(); // reserved
            uint dataOffset = reader.ReadUInt32();
            uint dataSize = reader.ReadUInt32();
            entries[i] = new ShaderPipelineEntry(backend, format, entryStages, dataOffset, dataSize);
        }

        long dataSectionStart = ShaderFileLayout.DataSectionStart(pipelineCount);

        var pipelines = new List<PipelineBlob>(pipelineCount);
        for (int i = 0; i < pipelineCount; i++)
        {
            stream.Position = dataSectionStart + entries[i].DataOffset;
            var blob = DeserializePipelineBlob(reader, entries[i]);
            pipelines.Add(blob);
        }

        return new CompiledShaderFile
        {
            FormatVersion = formatVersion,
            Stages = stages,
            Pipelines = pipelines,
        };
    }

    /// <summary>Reads one backend's pipeline blob, or null if the file has none for it.</summary>
    public static PipelineBlob? ReadPipeline(Stream stream, GraphicsBackend backend)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        Span<byte> magic = stackalloc byte[4];
        if (reader.Read(magic) != 4 || !magic.SequenceEqual(CompiledShaderFile.MagicBytes))
            throw new InvalidDataException("Not a valid .specshadecomp file (bad magic bytes)");

        RequireSupportedVersion(reader.ReadUInt16());
        reader.ReadByte();   // stages
        byte pipelineCount = reader.ReadByte();

        ShaderPipelineEntry? target = null;
        for (int i = 0; i < pipelineCount; i++)
        {
            var entryBackend = (GraphicsBackend)reader.ReadByte();
            var format = (ShaderDataFormat)reader.ReadByte();
            var entryStages = (ShaderStageFlags)reader.ReadByte();
            reader.ReadByte(); // reserved
            uint dataOffset = reader.ReadUInt32();
            uint dataSize = reader.ReadUInt32();

            if (entryBackend == backend)
            {
                target = new ShaderPipelineEntry(entryBackend, format, entryStages, dataOffset, dataSize);
                break;
            }
        }

        if (target is null)
            return null;

        // The scan stopped mid-table, so the stream position is not the data section start.
        stream.Position = ShaderFileLayout.DataSectionStart(pipelineCount) + target.Value.DataOffset;

        return DeserializePipelineBlob(reader, target.Value);
    }

    /// <summary>
    /// Reads one backend's pipeline blob out of a whole file already in memory,
    /// or null if the file has none for it. The result holds no reference to the span.
    /// </summary>
    /// <exception cref="InvalidDataException">Not a readable .specshadecomp file, or truncated.</exception>
    public static PipelineBlob? ReadPipeline(ReadOnlySpan<byte> file, GraphicsBackend backend)
    {
        var cursor = new SpanCursor(file);
        int pipelineCount = ReadFileHeader(ref cursor);

        ShaderPipelineEntry? target = null;
        for (int i = 0; i < pipelineCount; i++)
        {
            ShaderPipelineEntry entry = ReadEntry(ref cursor);
            if (entry.Backend != backend) continue;

            target = entry;
            break;
        }

        if (target is null)
            return null;

        // The scan stopped mid-table, so the cursor is not at the data section start.
        cursor.Seek(ShaderFileLayout.DataSectionStart(pipelineCount) + target.Value.DataOffset);

        return DeserializePipelineBlob(ref cursor, target.Value);
    }

    /// <summary>
    /// Every backend the file carries a blob for, in table order. Reads only the entry table.
    /// </summary>
    /// <exception cref="InvalidDataException">Not a readable .specshadecomp file, or truncated.</exception>
    public static GraphicsBackend[] ReadBackends(ReadOnlySpan<byte> file)
    {
        var cursor = new SpanCursor(file);
        int pipelineCount = ReadFileHeader(ref cursor);

        var backends = new GraphicsBackend[pipelineCount];
        for (int i = 0; i < pipelineCount; i++)
            backends[i] = ReadEntry(ref cursor).Backend;

        return backends;
    }

    public static CompiledShaderFile ReadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static PipelineBlob? ReadPipelineFromFile(string path, GraphicsBackend backend)
    {
        using var stream = File.OpenRead(path);
        return ReadPipeline(stream, backend);
    }

    // Exact match, not a floor: a compiled shader is a build output, recook it.
    private static ushort RequireSupportedVersion(ushort formatVersion)
    {
        if (formatVersion != EngineInfo.ShaderFormatVersion)
            throw new InvalidDataException(
                $"Compiled shader format version {formatVersion} cannot be read by this engine, "
                + $"which reads version {EngineInfo.ShaderFormatVersion}. Recompile (recook) the shader.");

        return formatVersion;
    }

    private static PipelineBlob DeserializePipelineBlob(BinaryReader reader, ShaderPipelineEntry entry)
    {
        byte[]? vertexData = null;
        byte[]? fragmentData = null;
        byte[]? geometryData = null;
        byte[]? computeData = null;

        if (entry.Stages.HasFlag(ShaderStageFlags.Vertex))
            vertexData = ReadStageData(reader);

        if (entry.Stages.HasFlag(ShaderStageFlags.Fragment))
            fragmentData = ReadStageData(reader);

        if (entry.Stages.HasFlag(ShaderStageFlags.Geometry))
            geometryData = ReadStageData(reader);

        if (entry.Stages.HasFlag(ShaderStageFlags.Compute))
            computeData = ReadStageData(reader);

        VertexInputElement[] vertexInputs = ReadVertexInputs(reader, entry.DataSize);

        byte[]? instancedVertexData = null;
        VertexInputElement[] instancedVertexInputs = [];
        if (reader.ReadByte() != 0)
        {
            instancedVertexData = ReadStageData(reader);
            instancedVertexInputs = ReadVertexInputs(reader, entry.DataSize);
        }

        return new PipelineBlob
        {
            Backend = entry.Backend,
            Format = entry.Format,
            Stages = entry.Stages,
            VertexData = vertexData,
            FragmentData = fragmentData,
            GeometryData = geometryData,
            ComputeData = computeData,
            VertexInputs = vertexInputs,
            InstancedVertexData = instancedVertexData,
            InstancedVertexInputs = instancedVertexInputs,
        };
    }

    private static byte[] ReadStageData(BinaryReader reader)
    {
        uint length = reader.ReadUInt32();
        return reader.ReadBytes((int)length);
    }

    private static VertexInputElement[] ReadVertexInputs(BinaryReader reader, uint blobSize)
    {
        uint count = reader.ReadUInt32();

        // Bound the count by the blob size before allocating for it.
        if (count > blobSize / ShaderFileLayout.VertexInputRecordSize)
            throw new InvalidDataException(
                $"Vertex input table declares {count} elements, more than the {blobSize}-byte pipeline blob can hold");

        var inputs = new VertexInputElement[count];
        Span<byte> record = stackalloc byte[ShaderFileLayout.VertexInputRecordSize];

        for (int i = 0; i < inputs.Length; i++)
        {
            if (reader.Read(record) != record.Length)
                throw new InvalidDataException("Truncated vertex input record");

            uint location = BinaryPrimitives.ReadUInt32LittleEndian(record);
            uint locationSpan = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
            uint componentCount = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
            var rate = (VertexInputRate)record[12];
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[13..]);

            byte[] nameBytes = reader.ReadBytes(nameLength);
            if (nameBytes.Length != nameLength)
                throw new InvalidDataException("Truncated vertex input name");

            inputs[i] = new VertexInputElement(
                Encoding.UTF8.GetString(nameBytes), location, locationSpan, componentCount, rate);
        }

        return inputs;
    }

    // Returns the pipeline count.
    private static int ReadFileHeader(ref SpanCursor cursor)
    {
        if (!cursor.Take(4).SequenceEqual(CompiledShaderFile.MagicBytes))
            throw new InvalidDataException("Not a valid .specshadecomp file (bad magic bytes)");

        RequireSupportedVersion(cursor.U16());
        cursor.U8();  // stages, restated per entry
        return cursor.U8();
    }

    private static ShaderPipelineEntry ReadEntry(ref SpanCursor cursor)
    {
        var backend = (GraphicsBackend)cursor.U8();
        var format = (ShaderDataFormat)cursor.U8();
        var stages = (ShaderStageFlags)cursor.U8();
        cursor.U8();  // reserved
        uint dataOffset = cursor.U32();
        uint dataSize = cursor.U32();
        return new ShaderPipelineEntry(backend, format, stages, dataOffset, dataSize);
    }

    private static PipelineBlob DeserializePipelineBlob(ref SpanCursor cursor, ShaderPipelineEntry entry)
    {
        byte[]? vertexData = null;
        byte[]? fragmentData = null;
        byte[]? geometryData = null;
        byte[]? computeData = null;

        if (entry.Stages.HasFlag(ShaderStageFlags.Vertex))
            vertexData = ReadStageData(ref cursor);

        if (entry.Stages.HasFlag(ShaderStageFlags.Fragment))
            fragmentData = ReadStageData(ref cursor);

        if (entry.Stages.HasFlag(ShaderStageFlags.Geometry))
            geometryData = ReadStageData(ref cursor);

        if (entry.Stages.HasFlag(ShaderStageFlags.Compute))
            computeData = ReadStageData(ref cursor);

        VertexInputElement[] vertexInputs = ReadVertexInputs(ref cursor, entry.DataSize);

        byte[]? instancedVertexData = null;
        VertexInputElement[] instancedVertexInputs = [];
        if (cursor.U8() != 0)
        {
            instancedVertexData = ReadStageData(ref cursor);
            instancedVertexInputs = ReadVertexInputs(ref cursor, entry.DataSize);
        }

        return new PipelineBlob
        {
            Backend = entry.Backend,
            Format = entry.Format,
            Stages = entry.Stages,
            VertexData = vertexData,
            FragmentData = fragmentData,
            GeometryData = geometryData,
            ComputeData = computeData,
            VertexInputs = vertexInputs,
            InstancedVertexData = instancedVertexData,
            InstancedVertexInputs = instancedVertexInputs,
        };
    }

    private static byte[] ReadStageData(ref SpanCursor cursor) => cursor.Take(cursor.U32()).ToArray();

    private static VertexInputElement[] ReadVertexInputs(ref SpanCursor cursor, uint blobSize)
    {
        uint count = cursor.U32();

        // Bound the count by the blob size before allocating for it.
        if (count > blobSize / ShaderFileLayout.VertexInputRecordSize)
            throw new InvalidDataException(
                $"Vertex input table declares {count} elements, more than the {blobSize}-byte pipeline blob can hold");

        var inputs = new VertexInputElement[count];
        for (int i = 0; i < inputs.Length; i++)
        {
            ReadOnlySpan<byte> record = cursor.Take(ShaderFileLayout.VertexInputRecordSize);

            uint location = BinaryPrimitives.ReadUInt32LittleEndian(record);
            uint locationSpan = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
            uint componentCount = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
            var rate = (VertexInputRate)record[12];
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[13..]);

            inputs[i] = new VertexInputElement(
                Encoding.UTF8.GetString(cursor.Take(nameLength)),
                location,
                locationSpan,
                componentCount,
                rate);
        }

        return inputs;
    }

    // Bounds-checked read position. A ref struct because the span may be a mapped
    // pack view, which must not outlive its ContentBlob.
    private ref struct SpanCursor
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _at;

        public SpanCursor(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _at = 0;
        }

        public void Seek(long position)
        {
            if (position < 0 || position > _bytes.Length)
                throw new InvalidDataException(
                    $"Compiled shader file seeks to {position}, outside its {_bytes.Length} bytes.");

            _at = (int)position;
        }

        // uint, so a corrupt 32-bit size cannot wrap into a small positive int.
        public ReadOnlySpan<byte> Take(uint count) =>
            count > int.MaxValue ? throw Truncated(count) : Take((int)count);

        public ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || _bytes.Length - _at < count) throw Truncated((uint)count);

            ReadOnlySpan<byte> slice = _bytes.Slice(_at, count);
            _at += count;
            return slice;
        }

        public byte U8() => Take(1)[0];

        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        private InvalidDataException Truncated(uint count) =>
            new($"Compiled shader file is truncated: {count} bytes wanted at offset {_at} of {_bytes.Length}.");
    }
}
