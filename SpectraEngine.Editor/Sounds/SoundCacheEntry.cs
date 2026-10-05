using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Text;

namespace SpectraEngine.Editor.Sounds;

// One cooked sound in the editor's cache, opened: what the cook read, what it
// said, and the cooked bytes still on disk behind the header. A sound the cook
// refused has an entry too, with no bytes, so it is not cooked again either.
//
// Little-endian:
//   u32 magic, u32 version
//   u128 key
//   i32 count, then per file the cook touched: string path, u8 kind, u128 hash
//   i32 count, then per note: u8 severity, string text
//   i64 length of the cooked sound, -1 when refused
//   u128 hash of the cooked sound
//   the cooked sound
internal sealed class SoundCacheEntry : IDisposable
{
    private const uint Magic = 0x45435353; // "SSCE"
    private const uint FormatVersion = 1;
    private const long Refused = -1;

    // A damaged count must not turn into a huge allocation.
    private const int MaxListLength = 4096;

    private readonly Stream _stream;
    private readonly long _soundLength;
    private readonly UInt128 _soundHash;

    private SoundCacheEntry(
        Stream stream,
        UInt128 key,
        RuleDependency[] dependencies,
        SoundCookNote[] notes,
        long soundLength,
        UInt128 soundHash)
    {
        _stream = stream;
        Key = key;
        Dependencies = dependencies;
        Notes = notes;
        _soundLength = soundLength;
        _soundHash = soundHash;
    }

    // The cook's key for the run that wrote this entry.
    public UInt128 Key { get; }

    public IReadOnlyList<RuleDependency> Dependencies { get; }

    public IReadOnlyList<SoundCookNote> Notes { get; }

    public bool HasSound => _soundLength != Refused;

    public static void Write(Stream stream, LooseSoundResult result)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(FormatVersion);
        WriteU128(writer, result.Key);

        writer.Write(result.Dependencies.Count);
        foreach (RuleDependency dependency in result.Dependencies)
        {
            writer.Write(dependency.Path);
            writer.Write((byte)dependency.Kind);
            WriteU128(writer, dependency.ContentHash);
        }

        writer.Write(result.Diagnostics.Count);
        foreach (CookDiagnostic diagnostic in result.Diagnostics)
        {
            SoundCookNote note = SoundCookNote.From(diagnostic);
            writer.Write((byte)note.Severity);
            writer.Write(note.Text);
        }

        byte[]? cooked = result.Cooked;
        writer.Write(cooked?.LongLength ?? Refused);
        WriteU128(writer, cooked is null ? UInt128.Zero : XxHash128.HashToUInt128(cooked));
        if (cooked is not null) writer.Write(cooked);
    }

    // Takes the stream over when it returns. Throws on a file that is not an
    // entry this build wrote, and the caller keeps the stream.
    public static SoundCacheEntry Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Not a cooked sound cache entry.");
        if (reader.ReadUInt32() != FormatVersion) throw new InvalidDataException("Written by another version.");

        UInt128 key = ReadU128(reader);

        var dependencies = new RuleDependency[ReadCount(reader)];
        for (int i = 0; i < dependencies.Length; i++)
        {
            string path = reader.ReadString();
            RuleDependencyKind kind = ToDependencyKind(reader.ReadByte());
            dependencies[i] = new RuleDependency(path, kind, ReadU128(reader));
        }

        var notes = new SoundCookNote[ReadCount(reader)];
        for (int i = 0; i < notes.Length; i++)
        {
            CookDiagnosticSeverity severity = ToSeverity(reader.ReadByte());
            notes[i] = new SoundCookNote(severity, reader.ReadString());
        }

        long soundLength = reader.ReadInt64();
        if (soundLength is < Refused or > int.MaxValue) throw new InvalidDataException("Bad sound length.");

        return new SoundCacheEntry(stream, key, dependencies, notes, soundLength, ReadU128(reader));
    }

    // The cooked sound, or null when the bytes on disk are not the ones that
    // were written. Call once: it reads from where the header ended.
    public ContentBlob? ReadSound()
    {
        if (!HasSound || _stream.Length - _stream.Position != _soundLength) return null;

        ContentBlob blob = ContentBlob.Rent((int)_soundLength, out Span<byte> destination);
        bool intact = false;

        try
        {
            _stream.ReadExactly(destination);
            intact = XxHash128.HashToUInt128(destination) == _soundHash;
        }
        catch (IOException)
        {
            // Unreadable is the same as damaged: the sound is cooked again.
        }

        if (intact) return blob;

        blob.Dispose();
        return null;
    }

    public void Dispose() => _stream.Dispose();

    private static int ReadCount(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > MaxListLength) throw new InvalidDataException("Bad list length.");
        return count;
    }

    // Two halves, so the bytes do not depend on the machine's byte order.
    private static void WriteU128(BinaryWriter writer, UInt128 value)
    {
        writer.Write((ulong)value);
        writer.Write((ulong)(value >> 64));
    }

    private static UInt128 ReadU128(BinaryReader reader)
    {
        ulong low = reader.ReadUInt64();
        ulong high = reader.ReadUInt64();
        return ((UInt128)high << 64) | low;
    }

    // No cast: an unknown value must not be taken for a known one.
    private static RuleDependencyKind ToDependencyKind(byte value) => value switch
    {
        (byte)RuleDependencyKind.Read => RuleDependencyKind.Read,
        (byte)RuleDependencyKind.ProbeFound => RuleDependencyKind.ProbeFound,
        (byte)RuleDependencyKind.ProbeMissing => RuleDependencyKind.ProbeMissing,
        _ => throw new InvalidDataException("Unknown dependency kind."),
    };

    private static CookDiagnosticSeverity ToSeverity(byte value) => value switch
    {
        (byte)CookDiagnosticSeverity.Info => CookDiagnosticSeverity.Info,
        (byte)CookDiagnosticSeverity.Warning => CookDiagnosticSeverity.Warning,
        (byte)CookDiagnosticSeverity.Error => CookDiagnosticSeverity.Error,
        _ => throw new InvalidDataException("Unknown severity."),
    };
}
