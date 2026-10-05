using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Text;

namespace Spectra.Kitchen.Cooking;

/// <summary>
/// What a kept cook of one loose sound was made from, so a later run can tell
/// without cooking whether it still stands. Stored with the cooked bytes.
/// </summary>
/// <param name="SoundFormatVersion">The <c>.saudio</c> version the sound was written in.</param>
/// <param name="Key">The cook's key for the run.</param>
/// <param name="Dependencies">The files the cook read or looked for, misses included.</param>
/// <param name="Diagnostics">What the cook said.</param>
/// <param name="CookedLength">Byte count of the cooked sound, or -1 when the cook refused it.</param>
/// <param name="CookedHash"><c>XxHash128</c> of the cooked sound, zero when refused.</param>
// Little-endian:
//   u32 magic, u32 version, u32 sound format version
//   u128 key
//   i32 count, then per file: string path, u8 kind, u128 hash
//   i32 count, then per diagnostic: u8 severity, string prefix, i32 number,
//     string message, string file (empty for none), i32 line, i32 column
//   i64 cooked length, u128 cooked hash
public sealed record LooseSoundStamp(
    int SoundFormatVersion,
    UInt128 Key,
    IReadOnlyList<RuleDependency> Dependencies,
    IReadOnlyList<CookDiagnostic> Diagnostics,
    long CookedLength,
    UInt128 CookedHash)
{
    private const uint Magic = 0x54534C53; // "SLST"
    private const uint FormatVersion = 1;
    private const long Refused = -1;

    // A damaged count must not turn into a huge allocation.
    private const int MaxListLength = 4096;

    /// <summary>Whether the cook wrote a sound. False for one it refused.</summary>
    public bool HasSound => CookedLength != Refused;

    /// <summary>The stamp for what <paramref name="result"/> cooked.</summary>
    public static LooseSoundStamp Of(LooseSoundResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        byte[]? cooked = result.Cooked;
        return new LooseSoundStamp(
            EngineInfo.AudioFormatVersion,
            result.Key,
            result.Dependencies,
            result.Diagnostics,
            cooked?.LongLength ?? Refused,
            cooked is null ? UInt128.Zero : XxHash128.HashToUInt128(cooked));
    }

    /// <summary>
    /// Whether cooking <paramref name="soundPath"/> now would give what this
    /// stamp records: the sound format is this engine's, the stamp is of that
    /// file, and nothing the cook touched has changed, appeared or gone.
    /// </summary>
    /// <param name="settings">Null compares against a project cook's defaults.</param>
    public bool Holds(string contentRoot, string soundPath, CookSettings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soundPath);

        if (SoundFormatVersion != EngineInfo.AudioFormatVersion || !IsCookOf(soundPath)) return false;

        return LooseSoundCook.CurrentKey(contentRoot, Dependencies, settings) == Key;
    }

    /// <summary>Whether <paramref name="cooked"/> is the sound this stamp was written for.</summary>
    public bool IsStampOf(ReadOnlySpan<byte> cooked) =>
        cooked.Length == CookedLength && XxHash128.HashToUInt128(cooked) == CookedHash;

    /// <summary>Writes the stamp at the stream's position.</summary>
    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(SoundFormatVersion);
        WriteU128(writer, Key);

        writer.Write(Dependencies.Count);
        foreach (RuleDependency dependency in Dependencies)
        {
            writer.Write(dependency.Path);
            writer.Write((byte)dependency.Kind);
            WriteU128(writer, dependency.ContentHash);
        }

        writer.Write(Diagnostics.Count);
        foreach (CookDiagnostic diagnostic in Diagnostics)
        {
            writer.Write((byte)diagnostic.Severity);
            writer.Write(diagnostic.Id.Prefix);
            writer.Write(diagnostic.Id.Number);
            writer.Write(diagnostic.Message);
            writer.Write(diagnostic.File ?? string.Empty);
            writer.Write(diagnostic.Line);
            writer.Write(diagnostic.Column);
        }

        writer.Write(CookedLength);
        WriteU128(writer, CookedHash);
    }

    /// <summary>
    /// Reads a stamp and leaves the stream just behind it, where a file that
    /// holds the sound too has its bytes.
    /// </summary>
    /// <exception cref="InvalidDataException">The bytes are not a stamp this build wrote.</exception>
    public static LooseSoundStamp Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        try
        {
            return Read(reader);
        }
        catch (Exception ex) when (ex is EndOfStreamException or FormatException or ArgumentException)
        {
            // Cut short, a bad string length, or a code no diagnostic may have.
            throw new InvalidDataException("Damaged sound stamp.", ex);
        }
    }

    private static LooseSoundStamp Read(BinaryReader reader)
    {
        if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Not a sound stamp.");
        if (reader.ReadUInt32() != FormatVersion) throw new InvalidDataException("Written by another version.");

        int soundFormatVersion = reader.ReadInt32();
        UInt128 key = ReadU128(reader);

        var dependencies = new RuleDependency[ReadCount(reader)];
        for (int i = 0; i < dependencies.Length; i++)
        {
            string path = reader.ReadString();
            RuleDependencyKind kind = ToDependencyKind(reader.ReadByte());
            dependencies[i] = new RuleDependency(path, kind, ReadU128(reader));
        }

        var diagnostics = new CookDiagnostic[ReadCount(reader)];
        for (int i = 0; i < diagnostics.Length; i++) diagnostics[i] = ReadDiagnostic(reader);

        long cookedLength = reader.ReadInt64();
        if (cookedLength < Refused) throw new InvalidDataException("Bad sound length.");

        return new LooseSoundStamp(
            soundFormatVersion, key, dependencies, diagnostics, cookedLength, ReadU128(reader));
    }

    private static CookDiagnostic ReadDiagnostic(BinaryReader reader)
    {
        byte severity = reader.ReadByte();
        string prefix = reader.ReadString();
        int number = reader.ReadInt32();
        string message = reader.ReadString();
        string file = reader.ReadString();
        int line = reader.ReadInt32();
        int column = reader.ReadInt32();

        CookDiagnosticId id = prefix == CookDiagnosticId.CookPrefix
            ? CookDiagnosticId.Cook(number)
            : CookDiagnosticId.Wrap(prefix, number);
        string? about = file.Length > 0 ? file : null;

        // No cast: an unknown value must not be taken for a known one.
        return severity switch
        {
            (byte)CookDiagnosticSeverity.Info => CookDiagnostic.Info(id, message, about, line, column),
            (byte)CookDiagnosticSeverity.Warning => CookDiagnostic.Warning(id, message, about, line, column),
            (byte)CookDiagnosticSeverity.Error => CookDiagnostic.Error(id, message, about, line, column),
            _ => throw new InvalidDataException("Unknown severity."),
        };
    }

    // A .wav and a .wave of one name share a cooked path, and so a stamp. One
    // cooked from the other file is unchanged by its own key and still not
    // this sound.
    private bool IsCookOf(string soundPath)
    {
        foreach (RuleDependency dependency in Dependencies)
        {
            if (string.Equals(dependency.Path, soundPath, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

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

    private static RuleDependencyKind ToDependencyKind(byte value) => value switch
    {
        (byte)RuleDependencyKind.Read => RuleDependencyKind.Read,
        (byte)RuleDependencyKind.ProbeFound => RuleDependencyKind.ProbeFound,
        (byte)RuleDependencyKind.ProbeMissing => RuleDependencyKind.ProbeMissing,
        _ => throw new InvalidDataException("Unknown dependency kind."),
    };
}
