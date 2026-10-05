using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Packs;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets.Audio;
using System;
using System.Collections.Generic;
using System.IO;

namespace Spectra.Kitchen.Cooking;

/// <summary>
/// Cooks every sound under a content folder into a folder of <c>.saudio</c>
/// files at the same relative paths. For a build that runs a game from loose
/// files, where nothing can cook a sound when it is first played.
/// </summary>
// Each cooked file has a stamp beside it. A sound is cooked again when its
// stamp no longer holds, and what the cook said about it is said again when
// it does. A cooked file whose sound is gone is removed.
public static class LooseSoundFolder
{
    /// <summary>What a cooked sound's stamp file adds to its name.</summary>
    public const string StampSuffix = ".stamp";

    /// <summary>
    /// Brings the cooked sounds under <paramref name="outputRoot"/> in step
    /// with the sounds under <paramref name="contentRoot"/>.
    /// </summary>
    /// <param name="settings">Null cooks with a project cook's defaults.</param>
    public static LooseSoundFolderResult Cook(string contentRoot, string outputRoot, CookSettings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        string source = Path.GetFullPath(contentRoot);
        string output = Path.GetFullPath(outputRoot);

        var log = new CookDiagnosticLog(settings?.Strict ?? false);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int cooked = 0, upToDate = 0;

        // Walk order is ordinal, so diagnostics come out the same every run.
        foreach (ContentFile file in ContentWalker.Walk(source))
        {
            if (!AudioRule.Handles(file.ContentPath)) continue;

            string cookedPath = AudioContentPath.CookedPathFor(file.ContentPath);
            if (!owners.TryAdd(cookedPath, file.ContentPath))
            {
                log.Add(CookDiagnostic.Error(
                    CookDiagnosticCodes.PackEntryCollision,
                    $"'{cookedPath}' is cooked from both '{owners[cookedPath]}' and '{file.ContentPath}'. " +
                    "One content path is one asset.",
                    file.FullPath));
                continue;
            }

            string target = Path.Combine(output, cookedPath.Replace('/', Path.DirectorySeparatorChar));
            if (StandingStamp(source, file.ContentPath, target, settings) is { } stamp)
            {
                // A warning must not go quiet, and --strict may refuse it now.
                int errors = log.ErrorCount;
                log.AddRange(stamp.Diagnostics);
                if (log.ErrorCount == errors) upToDate++;
                continue;
            }

            LooseSoundResult result = LooseSoundCook.Run(source, file.ContentPath, settings);
            log.AddRange(result.Diagnostics);

            if (result.Cooked is { } bytes && TryWrite(target, bytes, LooseSoundStamp.Of(result), log)) cooked++;
        }

        int removed = RemoveCookedWithNoSound(output, owners, log);

        return new LooseSoundFolderResult(cooked, upToDate, removed, log.Entries);
    }

    // Null when the sound has to be cooked: no stamp, one that no longer
    // holds, or a cooked file that is not the one the stamp was written for.
    private static LooseSoundStamp? StandingStamp(
        string contentRoot, string soundPath, string target, CookSettings? settings)
    {
        LooseSoundStamp stamp;
        try
        {
            using FileStream stream = File.OpenRead(target + StampSuffix);
            stamp = LooseSoundStamp.Read(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }

        if (!stamp.HasSound || !stamp.Holds(contentRoot, soundPath, settings)) return null;

        var cooked = new FileInfo(target);
        return cooked.Exists && cooked.Length == stamp.CookedLength ? stamp : null;
    }

    // The sound first. Stopped between the two, the stamp left behind is of
    // the sound's old files, so the sound is cooked again.
    private static bool TryWrite(string target, byte[] cooked, LooseSoundStamp stamp, CookDiagnosticLog log)
    {
        try
        {
            if (Path.GetDirectoryName(target) is { Length: > 0 } folder) Directory.CreateDirectory(folder);

            AtomicOutput.Write(target, stream => stream.Write(cooked));
            AtomicOutput.Write(target + StampSuffix, stamp.Write);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.OutputNotWritable, $"Could not write '{target}': {ex.Message}", target));
            return false;
        }
    }

    // A cooked file whose sound was deleted or renamed would still play. Only
    // a file with a stamp goes, so nothing this did not write is deleted.
    private static int RemoveCookedWithNoSound(
        string output, Dictionary<string, string> owners, CookDiagnosticLog log)
    {
        if (!Directory.Exists(output)) return 0;

        string[] stamps = Directory.GetFiles(
            output, "*" + SaudioFormat.FileExtension + StampSuffix, SearchOption.AllDirectories);
        Array.Sort(stamps, StringComparer.Ordinal);

        int removed = 0;
        foreach (string stamp in stamps)
        {
            string cooked = stamp[..^StampSuffix.Length];
            string cookedPath = Path.GetRelativePath(output, cooked).Replace(Path.DirectorySeparatorChar, '/');
            if (owners.ContainsKey(cookedPath)) continue;

            try
            {
                File.Delete(cooked);
                File.Delete(stamp);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Add(CookDiagnostic.Error(
                    CookDiagnosticCodes.OutputNotWritable, $"Could not remove '{cooked}': {ex.Message}", cooked));
            }
        }

        return removed;
    }
}
