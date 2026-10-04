using SpectraEngine.Core.Projects;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace Spectra.Kitchen.Tests;

// A real project folder in a temp directory. Not an in-memory fake: path
// normalisation, separators and walk order are what a cook gets wrong.
internal sealed class TempProject : IDisposable
{
    private readonly List<IDisposable> _open = [];

    public TempProject(string name = "TestGame")
    {
        Root = Path.Combine(Path.GetTempPath(), $"spectra_cook_{Guid.NewGuid():N}");
        Layout = ProjectLayout.Create(Root, name);
    }

    public string Root { get; }

    public ProjectLayout Layout { get; }

    public string CookedPath => Layout.CookedPath;

    public byte[] WriteAsset(string contentPath, byte[] bytes)
    {
        string full = Path.Combine(
            Layout.AssetsPath, contentPath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return bytes;
    }

    public byte[] WriteAsset(string contentPath, string text) =>
        WriteAsset(contentPath, Encoding.UTF8.GetBytes(text));

    public static byte[] Bytes(int length, byte seed = 0)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)((i * 31) + seed);
        return bytes;
    }

    // A decodable PNG: the cook decodes .png, so random bytes are a build error.
    // Asymmetric on both axes so a flip, mirror or transpose shows.
    public static byte[] Png(int width = 8, int height = 8, byte seed = 0, int channels = 3)
    {
        // 0 greyscale, 2 truecolour. Channel count decides BC4 or BC7 in the cook.
        byte colourType = channels switch
        {
            1 => 0,
            3 => 2,
            _ => throw new ArgumentOutOfRangeException(
                nameof(channels), channels, "This writer emits greyscale or truecolour."),
        };

        var scanlines = new byte[height * (1 + width * channels)];
        for (int y = 0, at = 0; y < height; y++)
        {
            scanlines[at++] = 0;  // filter type 0: none
            for (int x = 0; x < width; x++)
            {
                scanlines[at++] = (byte)(seed + x * 17);
                if (channels == 1) continue;

                scanlines[at++] = (byte)(seed + y * 29);
                scanlines[at++] = (byte)(seed + x * 7 + y * 3);
            }
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(scanlines);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;   // bit depth
        header[9] = colourType;
        header[10] = 0;  // deflate
        header[11] = 0;  // adaptive filtering
        header[12] = 0;  // no interlace

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    // A 16-bit PCM WAV written from the RIFF spec, not through engine code, so
    // the cooker is not checked against itself.
    // loopEnd is inclusive, as the smpl chunk stores it.
    // A sine, not noise: a tone still looks like a tone after resampling.
    public static byte[] Wav(
        int frames = 64,
        int sampleRate = 48_000,
        int channels = 1,
        int seed = 0,
        long loopStart = -1,
        long loopEnd = -1,
        uint loopType = 0)
    {
        var samples = new short[frames * channels];
        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                // Different phase per channel, so a channel swap shows.
                double phase = (frame + seed + channel * 8) * (2 * Math.PI / 32);
                samples[frame * channels + channel] = (short)(Math.Sin(phase) * 12000);
            }
        }

        byte[] data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), samples[i]);

        var fmt = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(fmt, 1);                       // WAVE_FORMAT_PCM
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(2), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(4), (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(8), (uint)(sampleRate * channels * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(12), (ushort)(channels * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(14), 16);

        using var body = new MemoryStream();
        body.Write(Encoding.ASCII.GetBytes("WAVE"));
        WriteRiffChunk(body, "fmt ", fmt);
        WriteRiffChunk(body, "data", data);

        if (loopEnd >= 0)
        {
            var smpl = new byte[36 + 24];
            BinaryPrimitives.WriteUInt32LittleEndian(smpl.AsSpan(28), 1);        // one loop
            BinaryPrimitives.WriteUInt32LittleEndian(smpl.AsSpan(36 + 4), loopType);
            BinaryPrimitives.WriteUInt32LittleEndian(smpl.AsSpan(36 + 8), (uint)Math.Max(0, loopStart));
            BinaryPrimitives.WriteUInt32LittleEndian(smpl.AsSpan(36 + 12), (uint)loopEnd);
            WriteRiffChunk(body, "smpl", smpl);
        }

        byte[] payload = body.ToArray();

        using var file = new MemoryStream();
        file.Write(Encoding.ASCII.GetBytes("RIFF"));

        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)payload.Length);
        file.Write(size);
        file.Write(payload);
        return file.ToArray();
    }

    // An odd body gets a pad byte that is not counted in the size.
    private static void WriteRiffChunk(Stream wav, string id, byte[] body)
    {
        wav.Write(Encoding.ASCII.GetBytes(id));

        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)body.Length);
        wav.Write(size);
        wav.Write(body);

        if ((body.Length & 1) != 0) wav.WriteByte(0);
    }

    // The CRC covers the type and the data, not the data alone.
    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);

        byte[] typed = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type).CopyTo(typed, 0);
        data.CopyTo(typed, 4);
        png.Write(typed);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.HashToUInt32(typed));
        png.Write(crc);
    }

    // Disposed before the folder is deleted.
    public T Track<T>(T disposable) where T : IDisposable
    {
        _open.Add(disposable);
        return disposable;
    }

    public void Dispose()
    {
        // Mapped packs first: Windows cannot delete a folder holding a mapped file.
        for (int i = _open.Count - 1; i >= 0; i--) _open[i].Dispose();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Don't fail a test on its own cleanup.
        }
    }
}
