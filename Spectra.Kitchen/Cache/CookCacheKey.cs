using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Packs;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace Spectra.Kitchen.Cache;

/// <summary>
/// The identity of one rule run: <c>XxHash128</c> over a canonical byte stream of
/// everything that can change what that rule emits.
/// </summary>
// The stream, little-endian, str = u32 UTF-8 byte count + bytes:
//
//   "SCOOK\0"          6-byte tag
//   u32 CookerVersion
//   u32 RuleKindId
//   u32 RuleVersion
//   u32 settingCount   per setting, sorted ordinal by key: str key, str value
//   u32 toolCount      per tool, fixed order: str name, str value
//   u32 inputCount     per input, declared order: u128 pathId, u128 contentHash
//   u32 missingCount   per missing probe, declared order: u128 pathId
//
// Length prefixes, not separators, so two streams cannot collide by concatenation.
// Inputs stay in the rule's first-access order: sorting would hide an access
// order that depends on scheduling.
// A path goes in as its pack asset id, so two spellings are one dependency.
// A found probe is an input with a zero hash, a missed one is in the trailing
// list. A file appearing where a rule looked changes both counts.
public static class CookCacheKey
{
    /// <summary>
    /// Version of the key's composition. Raise by hand when the stream changes
    /// shape or when cooker-wide machinery can change what rules emit.
    /// </summary>
    public const uint CookerVersion = 1;

    /// <summary>
    /// Version of the block-compression encoder, carried as a tool version.
    /// Raise by hand whenever the encoder can change its output.
    /// </summary>
    public const string EncoderVersion = "none";

    private static ReadOnlySpan<byte> Tag => "SCOOK\0"u8;

    /// <summary>The key of one rule run over <paramref name="dependencies"/>.</summary>
    public static UInt128 Compute(
        RuleKind kind,
        int ruleVersion,
        CookSettingKeys declaredSettings,
        CookSettings settings,
        IReadOnlyList<RuleDependency> dependencies) =>
        XxHash128.HashToUInt128(
            BuildCanonicalStream(kind, ruleVersion, declaredSettings, settings, dependencies));

    /// <summary>
    /// The canonical bytes <see cref="Compute"/> hashes. Public so tests can
    /// check which field moved a key.
    /// </summary>
    public static byte[] BuildCanonicalStream(
        RuleKind kind,
        int ruleVersion,
        CookSettingKeys declaredSettings,
        CookSettings settings,
        IReadOnlyList<RuleDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dependencies);

        var stream = new KeyStream();

        stream.Raw(Tag);
        stream.U32(CookerVersion);
        stream.U32((uint)kind);
        stream.U32((uint)ruleVersion);

        List<KeyValuePair<string, string>> pairs = CookSettingsDigest.Describe(settings, declaredSettings);
        stream.U32((uint)pairs.Count);
        for (int i = 0; i < pairs.Count; i++)
        {
            stream.Str(pairs[i].Key);
            stream.Str(pairs[i].Value);
        }

        WriteToolVersions(stream);

        int inputs = 0, missing = 0;
        for (int i = 0; i < dependencies.Count; i++)
        {
            if (dependencies[i].IsMissing) missing++;
            else inputs++;
        }

        stream.U32((uint)inputs);
        for (int i = 0; i < dependencies.Count; i++)
        {
            RuleDependency dependency = dependencies[i];
            if (dependency.IsMissing) continue;

            stream.U128(PackAssetId.FromNormalized(dependency.Path));
            stream.U128(dependency.ContentHash);
        }

        stream.U32((uint)missing);
        for (int i = 0; i < dependencies.Count; i++)
        {
            RuleDependency dependency = dependencies[i];
            if (!dependency.IsMissing) continue;

            stream.U128(PackAssetId.FromNormalized(dependency.Path));
        }

        return stream.ToArray();
    }

    // Append only. Count and order are hashed, so reordering re-keys every
    // cached artifact.
    private static void WriteToolVersions(KeyStream stream)
    {
        stream.U32(6);

        Tool(stream, "encoder", EncoderVersion);
        Tool(stream, "shaderFormat", EngineInfo.ShaderFormatVersion);
        Tool(stream, "mapFormat", EngineInfo.MapFormatVersion);
        Tool(stream, "geometryFormat", EngineInfo.GeometryFormatVersion);
        Tool(stream, "packFormat", EngineInfo.PackFormatVersion);

        // The encoder's output depends on the CPU's instruction set.
        Tool(stream, "isa", InstructionSetBaseline.Token);
    }

    private static void Tool(KeyStream stream, string name, string value)
    {
        stream.Str(name);
        stream.Str(value);
    }

    private static void Tool(KeyStream stream, string name, uint value) =>
        Tool(stream, name, value.ToString(CultureInfo.InvariantCulture));

    private static void Tool(KeyStream stream, string name, int value) =>
        Tool(stream, name, value.ToString(CultureInfo.InvariantCulture));

    private sealed class KeyStream
    {
        private readonly ArrayBufferWriter<byte> _bytes = new(256);

        public void Raw(ReadOnlySpan<byte> value) => _bytes.Write(value);

        public void U32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_bytes.GetSpan(sizeof(uint)), value);
            _bytes.Advance(sizeof(uint));
        }

        // Two explicit halves so the bytes are the same on a big-endian machine.
        public void U128(UInt128 value)
        {
            Span<byte> span = _bytes.GetSpan(16);
            BinaryPrimitives.WriteUInt64LittleEndian(span, (ulong)value);
            BinaryPrimitives.WriteUInt64LittleEndian(span[8..], (ulong)(value >> 64));
            _bytes.Advance(16);
        }

        public void Str(string value)
        {
            int count = Encoding.UTF8.GetByteCount(value);
            U32((uint)count);

            if (count == 0) return;

            Encoding.UTF8.GetBytes(value, _bytes.GetSpan(count));
            _bytes.Advance(count);
        }

        public byte[] ToArray() => _bytes.WrittenSpan.ToArray();
    }
}
