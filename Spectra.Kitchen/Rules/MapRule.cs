using System;
using System.Collections.Generic;
using System.IO;
using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Bakes a <c>.smap</c> bundle into a <c>.scmap</c>, so a shipped game runs no CSG
/// at load. A map that cannot be compiled emits nothing.
/// </summary>
public sealed class MapRule : IRule
{
    /// <inheritdoc/>
    public RuleKind Kind => RuleKind.Map;

    /// <inheritdoc/>
    public int Version => 2;

    /// <inheritdoc/>
    public CookSettingKeys SettingsRead => CookSettingKeys.KeepBrushSource;

    /// <inheritdoc/>
    public void Cook(IRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string bundle = context.SourcePath;
        string documentPath = bundle + '/' + MapFormat.DocumentFileName;

        // The document comes out of the same pass as the digest's bytes, so the
        // two cannot disagree about what the bundle holds.
        var files = new List<(string Path, byte[] Bytes)>();
        byte[]? documentBytes = null;

        foreach (string file in context.ListFiles(bundle))
        {
            string relative = file[(bundle.Length + 1)..];

            // Skips per-user editor state: it would change the digest per
            // developer and miss the cache on every camera move.
            if (!MapBundleDigest.IsSourceFile(relative)) continue;

            byte[] bytes = context.Read(file);
            files.Add((relative, bytes));

            if (file.Equals(documentPath, StringComparison.OrdinalIgnoreCase)) documentBytes = bytes;
        }

        if (documentBytes is null)
        {
            // Read only to record the miss. It throws.
            context.Read(documentPath);
            return;
        }

        MapDocument document;
        try
        {
            document = MapReader.Read(documentBytes);
        }
        catch (MapFormatException ex)
        {
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.MapDocumentMalformed,
                $"'{documentPath}' is not a readable map: {ex.Message}",
                documentPath));

            return;
        }

        byte[]? compiled;
        try
        {
            compiled = ScmapBake.Bake(
                document,
                MapBundleDigest.Compute(files),
                context.KeepBrushSource,
                context.Report,
                bundle);
        }
        catch (MapFormatException ex)
        {
            // A plane set Brush's constructor rejects. Fatal: it is a hole in the world.
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.MapBrushRefused,
                $"'{bundle}' has a brush this engine cannot build: {ex.Message}",
                documentPath));

            return;
        }

        if (compiled is null) return;

        context.Emit(CookedPath(bundle), compiled, PackEntryKind.Map);
    }

    /// <summary>The content path a bundle's compiled map is emitted and resolved under.</summary>
    public static string CookedPath(string bundlePath) => CompiledMapPath.For(bundlePath);
}
