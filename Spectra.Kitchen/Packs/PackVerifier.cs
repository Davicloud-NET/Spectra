using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Assets.Images;
using SpectraEngine.Core.Assets.Models;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraEngine.Core.Maps.Compiled;
using System;
using System.Collections.Generic;
using System.IO;

namespace Spectra.Kitchen.Packs;

/// <summary>What one <c>verify</c> run found.</summary>
public sealed class PackVerifyResult
{
    /// <summary>The pack that was verified.</summary>
    public required string PackPath { get; init; }

    /// <summary>Everything the run has to say, in the order it found it.</summary>
    public required IReadOnlyList<CookDiagnostic> Diagnostics { get; init; }

    /// <summary>Entries whose payload was opened and measured.</summary>
    public int EntriesChecked { get; init; }

    /// <summary>Tombstones, which have no payload to check.</summary>
    public int TombstonesSkipped { get; init; }

    /// <summary>In-pack references resolved, across every format that expresses one.</summary>
    public int ReferencesChecked { get; init; }

    /// <summary>Uncompressed bytes decoded.</summary>
    public long PayloadBytes { get; init; }

    /// <summary>How many diagnostics are errors.</summary>
    public int ErrorCount { get; init; }

    /// <summary>How many diagnostics are warnings.</summary>
    public int WarningCount { get; init; }

    /// <summary>Whether the pack passed.</summary>
    public bool Succeeded => ErrorCount == 0;
}

/// <summary>
/// Checks that a written <c>.spack</c> is one a shipped game can run on: every
/// payload decodes, every in-pack reference resolves, the entry table is sorted,
/// and the digest agrees.
/// </summary>
// References resolve through a strict ContentSourceStack holding only the pack.
// Loose files mounted beside it would answer for content the pack is missing.
// Uses PackSource, the reader a shipped game uses. Severities come from CookGate,
// so the cook and the verifier give one verdict for one code.
public static class PackVerifier
{
    /// <summary>
    /// Verifies the pack at <paramref name="packPath"/>. Problems with the pack's
    /// content come back as diagnostics, never as exceptions.
    /// </summary>
    /// <param name="packPath">The <c>.spack</c> to verify.</param>
    /// <param name="logger">Where the mount reports its own trouble.</param>
    /// <param name="targets">
    /// The backends the pack was cooked for. Null expects the union of the
    /// backends its own shaders carry, which is a weaker check.
    /// </param>
    /// <param name="strict">Promotes warn-by-default diagnostics to errors, as in a cook.</param>
    /// <exception cref="IOException">The file could not be opened.</exception>
    public static PackVerifyResult Verify(
        string packPath,
        ILogger? logger = null,
        IReadOnlyList<GraphicsBackend>? targets = null,
        bool strict = false)
    {
        ArgumentNullException.ThrowIfNull(packPath);

        string file = Path.GetFullPath(packPath);

        var diagnostics = new CookDiagnosticLog(strict);

        PackContents contents;
        try
        {
            contents = PackContents.Read(file);
        }
        catch (PackMountException ex)
        {
            diagnostics.Add(CookDiagnostic.Error(CookDiagnosticCodes.PackNotMountable, ex.Message, file));
            return Finish(file, diagnostics, 0, 0, 0, 0);
        }

        CheckEntryOrder(contents, diagnostics);

        // Mounted after the table check, so an out-of-order table is reported by
        // entry name and not only as the mount's refusal.
        PackSource pack;
        try
        {
            pack = new PackSource(logger ?? NullLogger.Instance, file);
        }
        catch (PackMountException ex)
        {
            diagnostics.Add(CookDiagnostic.Error(CookDiagnosticCodes.PackNotMountable, ex.Message, file));
            return Finish(file, diagnostics, 0, 0, 0, 0);
        }

        int payloads = 0, tombstones = 0, references = 0;
        long payloadBytes = 0;

        // Judged after the walk: the expected backends may be the union over all shaders.
        var shaders = new List<ShaderEntry>();

        using (pack)
        {
            var strictStack = new ContentSourceStack(strict: true);
            strictStack.Mount(pack);

            for (int i = 0; i < contents.Entries.Count; i++)
            {
                PackEntry entry = contents.Entries[i];
                if (entry.IsTombstone)
                {
                    tombstones++;
                    continue;
                }

                string name = contents.NameOf(i);
                if (name.Length == 0)
                {
                    diagnostics.Add(CookDiagnostic.Warning(
                        CookDiagnosticCodes.PackEntryNotVerifiable,
                        $"Entry {i} (id {entry.AssetId:X32}) carries no name, so there is no path to ask the " +
                        "reader for and its payload was not decoded. Cook with the name table on.",
                        file));
                    continue;
                }

                // Through the pack, not the strict stack: the entry is in the table,
                // so false here can only mean the payload did not decode.
                if (!pack.TryOpen(name, out ContentBlob? blob))
                {
                    diagnostics.Add(CookDiagnostic.Error(
                        CookDiagnosticCodes.PackEntryUnreadable,
                        $"'{name}' is in the entry table and its payload did not decode " +
                        $"({entry.StoredSize} stored bytes, codec {entry.EntryCodec}). The digest agrees, so " +
                        "these bytes were written this way rather than corrupted since.",
                        file));
                    continue;
                }

                using (blob)
                {
                    payloads++;
                    payloadBytes += blob.Length;

                    if ((ulong)blob.Length != entry.UncompressedSize)
                    {
                        diagnostics.Add(CookDiagnostic.Error(
                            CookDiagnosticCodes.PackEntryUnreadable,
                            $"'{name}' declares {entry.UncompressedSize} uncompressed bytes and decoded to " +
                            $"{blob.Length}.",
                            file));
                        continue;
                    }

                    references += CheckReferences(name, blob.Span, strictStack, diagnostics, file);
                    CheckImage(name, blob.Span, diagnostics, file);
                    CheckAudio(name, blob.Span, diagnostics, file);
                    CollectShader(name, blob.Span, shaders, diagnostics, file);
                }
            }
        }

        CheckShaders(shaders, targets, diagnostics, file);

        return Finish(file, diagnostics, payloads, tombstones, references, payloadBytes);
    }

    private readonly record struct ShaderEntry(string Name, GraphicsBackend[] Backends);

    // Resolves the cross-asset references one entry names; returns how many.
    // One arm per cooked format, each using the engine's own reader and
    // reporting in that format's diagnostic band. A raw-copied .obj/.mtl pair is
    // not checked: the importer resolves it at load.
    private static int CheckReferences(
        string name,
        ReadOnlySpan<byte> payload,
        ContentSourceStack strictStack,
        CookDiagnosticLog diagnostics,
        string packFile)
    {
        if (ModelContentPath.IsCooked(name))
            return CheckModel(name, payload, strictStack, diagnostics, packFile);

        if (name.EndsWith(ScmapFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
            return CheckCompiledMap(name, payload, strictStack, diagnostics, packFile);

        if (!name.EndsWith(MaterialParser.FileExtension, StringComparison.OrdinalIgnoreCase))
            return 0;

        MaterialDefinition material = MaterialParser.ParseUtf8(payload, name);

        foreach (string warning in material.Warnings)
            diagnostics.Add(CookDiagnostic.Warning(CookDiagnosticCodes.MaterialFileMalformed, warning, packFile));

        int resolved = 0;
        foreach (MaterialTextureSlot slot in material.Textures)
        {
            resolved++;

            try
            {
                // The material names the .png; the pack holds the cooked .simage.
                string imagePath = ImageContentPath.Resolve(strictStack, slot.TexturePath);
                if (strictStack.TryOpen(imagePath, out ContentBlob? texture))
                {
                    texture.Dispose();
                    continue;
                }
            }
            catch (FileNotFoundException)
            {
                // The strict stack throws on a miss. Reported below.
            }

            // Same code MaterialRule reports at cook time.
            diagnostics.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.MaterialTextureMissing,
                $"'{name}' binds sampler '{slot.Name}' to '{slot.TexturePath}', which is not in this pack. " +
                "The running engine would show the magenta placeholder and carry on; a shipped build would " +
                "ship that.",
                packFile));
        }

        return resolved;
    }

    // A cooked model must load and every material it names must be in the pack.
    // A submesh naming no material is fine here: the cook already reported it (SC3002).
    private static int CheckModel(
        string name,
        ReadOnlySpan<byte> payload,
        ContentSourceStack strictStack,
        CookDiagnosticLog diagnostics,
        string packFile)
    {
        SmodelModel model;
        try
        {
            model = SmodelReader.Read(payload, name);
        }
        catch (SmodelFormatException ex)
        {
            diagnostics.Add(CookDiagnostic.Error(CookDiagnosticCodes.ModelFileUnreadable, ex.Message, packFile));
            return 0;
        }

        int resolved = 0;
        for (int i = 0; i < model.Submeshes.Length; i++)
        {
            SmodelSubmesh submesh = model.Submeshes[i];
            if (!submesh.HasMaterial) continue;

            resolved++;
            string material = model.GetName(submesh.MaterialNameOffset);

            try
            {
                if (strictStack.TryOpen(material, out ContentBlob? blob))
                {
                    blob.Dispose();
                    continue;
                }
            }
            catch (FileNotFoundException)
            {
                // The strict stack throws on a miss. Reported below.
            }

            diagnostics.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.ModelMaterialMissing,
                $"'{name}' submesh {i} names material '{material}', which is not in this pack. The running " +
                "engine would bind the default material and carry on; a shipped build would ship a grey " +
                "prop.",
                packFile));
        }

        return resolved;
    }

    // A compiled map must load and every asset it names must be in the pack.
    private static int CheckCompiledMap(
        string name,
        ReadOnlySpan<byte> payload,
        ContentSourceStack strictStack,
        CookDiagnosticLog diagnostics,
        string packFile)
    {
        ScmapDocument map;
        try
        {
            map = ScmapReader.Read(payload, name);
        }
        catch (ScmapFormatException ex)
        {
            diagnostics.Add(CookDiagnostic.Error(CookDiagnosticCodes.MapFileUnreadable, ex.Message, packFile));
            return 0;
        }

        int resolved = 0;
        for (int i = 0; i < map.Assets.Length; i++)
        {
            PackEntryKind kind = map.Assets[i].AssetKind;
            string path = map.AssetPath(i);
            if (path.Length == 0) continue;

            resolved++;

            // Rows name the authored path; models and images resolve to their
            // cooked files. Any other kind is looked up as written, not skipped.
            string wanted = kind switch
            {
                PackEntryKind.Model => ModelContentPath.Resolve(strictStack, path),
                PackEntryKind.Image => ImageContentPath.Resolve(strictStack, path),
                _ => path,
            };

            try
            {
                if (strictStack.TryOpen(wanted, out ContentBlob? asset))
                {
                    asset.Dispose();
                    continue;
                }
            }
            catch (FileNotFoundException)
            {
                // The strict stack throws on a miss. Reported below.
            }

            diagnostics.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.MapAssetMissing,
                $"'{name}' names {Describe(kind)} '{path}', which is not in this pack. The running engine " +
                "would bind the default material and carry on; a shipped build would ship a grey level.",
                packFile));
        }

        return resolved;
    }

    private static string Describe(PackEntryKind kind) => kind switch
    {
        PackEntryKind.Material => "material",
        PackEntryKind.Model => "model",
        PackEntryKind.Image => "texture",
        PackEntryKind.Shader => "shader",
        PackEntryKind.Audio => "sound",
        _ => $"a {kind} asset",
    };

    // The engine's reader must accept a cooked image. The digest cannot see a
    // wrong profile version or a bad level index.
    private static void CheckImage(
        string name, ReadOnlySpan<byte> payload, CookDiagnosticLog diagnostics, string packFile)
    {
        if (!ImageContentPath.IsCooked(name)) return;

        try
        {
            _ = SimageReader.Read(payload, name);
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add(CookDiagnostic.Error(CookDiagnosticCodes.ImageFileUnreadable, ex.Message, packFile));
        }
    }

    // Same as CheckImage, for a cooked sound.
    private static void CheckAudio(
        string name, ReadOnlySpan<byte> payload, CookDiagnosticLog diagnostics, string packFile)
    {
        if (!AudioContentPath.IsCooked(name)) return;

        try
        {
            _ = SaudioReader.Read(payload, name);
        }
        catch (SaudioFormatException ex)
        {
            diagnostics.Add(CookDiagnostic.Error(CookDiagnosticCodes.AudioFileUnreadable, ex.Message, packFile));
        }
    }

    // Records which backends a cooked shader carries. Reads only the entry table.
    private static void CollectShader(
        string name,
        ReadOnlySpan<byte> payload,
        List<ShaderEntry> shaders,
        CookDiagnosticLog diagnostics,
        string packFile)
    {
        if (!name.EndsWith(ShaderRule.CookedExtension, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            shaders.Add(new ShaderEntry(name, ShaderFileReader.ReadBackends(payload)));
        }
        catch (InvalidDataException ex)
        {
            // An error here, though the engine would fall back to compiling from source.
            diagnostics.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.ShaderFileUnreadable,
                $"'{name}' is a cooked shader this engine's reader refuses: {ex.Message}",
                packFile));
        }
    }

    // Every cooked shader must carry a blob for every expected backend. A pack
    // does not record its targets, so without a target list the expectation is
    // the union over its own shaders. That misses a pack that is uniformly one
    // backend short.
    private static void CheckShaders(
        List<ShaderEntry> shaders,
        IReadOnlyList<GraphicsBackend>? targets,
        CookDiagnosticLog diagnostics,
        string packFile)
    {
        if (shaders.Count == 0) return;

        List<GraphicsBackend> expected = targets is { Count: > 0 }
            ? [.. targets]
            : UnionOfBackends(shaders);

        for (int i = 0; i < shaders.Count; i++)
        {
            ShaderEntry shader = shaders[i];
            for (int j = 0; j < expected.Count; j++)
            {
                if (Array.IndexOf(shader.Backends, expected[j]) >= 0) continue;

                diagnostics.Add(CookDiagnostic.Error(
                    CookDiagnosticCodes.ShaderBackendMissing,
                    $"'{shader.Name}' carries no blob for {CookSettingsDigest.ToWire(expected[j])}. " +
                    "The running engine would compile that shader from source and render correctly; a " +
                    "shipped build would ship the compiler's cost.",
                    packFile));
            }
        }
    }

    // First-appearance order, so diagnostics follow the order the pack was cooked in.
    private static List<GraphicsBackend> UnionOfBackends(List<ShaderEntry> shaders)
    {
        var union = new List<GraphicsBackend>(4);
        for (int i = 0; i < shaders.Count; i++)
        {
            GraphicsBackend[] backends = shaders[i].Backends;
            for (int j = 0; j < backends.Length; j++)
            {
                if (!union.Contains(backends[j])) union.Add(backends[j]);
            }
        }

        return union;
    }

    // Ids must be strictly ascending: the reader binary-searches the table, so
    // an equal or descending pair makes lookups miss.
    private static void CheckEntryOrder(PackContents contents, CookDiagnosticLog diagnostics)
    {
        for (int i = 1; i < contents.Entries.Count; i++)
        {
            UInt128 previous = contents.Entries[i - 1].AssetId;
            UInt128 current = contents.Entries[i].AssetId;
            if (current > previous) continue;

            string what = current == previous
                ? $"share asset id {current:X32}"
                : $"are out of order: {current:X32} does not sit above {previous:X32}";

            diagnostics.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.PackEntryTableUnsorted,
                $"Entries {i - 1} ('{Describe(contents, i - 1)}') and {i} ('{Describe(contents, i)}') {what}.",
                contents.Path));
        }
    }

    private static string Describe(PackContents contents, int index)
    {
        string name = contents.NameOf(index);
        return name.Length > 0 ? name : "unnamed";
    }

    private static PackVerifyResult Finish(
        string file,
        CookDiagnosticLog diagnostics,
        int entriesChecked,
        int tombstones,
        int references,
        long payloadBytes)
    {
        return new PackVerifyResult
        {
            PackPath = file,
            Diagnostics = diagnostics.Entries,
            EntriesChecked = entriesChecked,
            TombstonesSkipped = tombstones,
            ReferencesChecked = references,
            PayloadBytes = payloadBytes,
            ErrorCount = diagnostics.ErrorCount,
            WarningCount = diagnostics.WarningCount,
        };
    }
}
