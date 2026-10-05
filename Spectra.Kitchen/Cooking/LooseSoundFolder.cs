using Spectra.Kitchen.Audio;
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
// A sound is skipped by file time, the way a build tool skips: its cooked file
// is at least as new as the sound and the label file beside it. A label file
// that was deleted, or a cook that changed, is not noticed. Delete the cooked
// files to cook everything again.
public static class LooseSoundFolder
{
    /// <summary>
    /// Cooks the sounds under <paramref name="contentRoot"/> that are newer
    /// than their cooked file under <paramref name="outputRoot"/>.
    /// </summary>
    /// <param name="settings">Null cooks with a project cook's defaults.</param>
    public static LooseSoundFolderResult Cook(string contentRoot, string outputRoot, CookSettings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        string source = Path.GetFullPath(contentRoot);
        string output = Path.GetFullPath(outputRoot);

        var diagnostics = new List<CookDiagnostic>();
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int cooked = 0, upToDate = 0;

        // Walk order is ordinal, so diagnostics come out the same every run.
        foreach (ContentFile file in ContentWalker.Walk(source))
        {
            if (!AudioRule.Handles(file.ContentPath)) continue;

            string cookedPath = AudioContentPath.CookedPathFor(file.ContentPath);
            if (!owners.TryAdd(cookedPath, file.ContentPath))
            {
                diagnostics.Add(CookDiagnostic.Error(
                    CookDiagnosticCodes.PackEntryCollision,
                    $"'{cookedPath}' is cooked from both '{owners[cookedPath]}' and '{file.ContentPath}'. " +
                    "One content path is one asset.",
                    file.FullPath));
                continue;
            }

            string target = Path.Combine(output, cookedPath.Replace('/', Path.DirectorySeparatorChar));
            if (IsUpToDate(target, file.FullPath, LabelFileOf(source, file.ContentPath)))
            {
                upToDate++;
                continue;
            }

            LooseSoundResult result = LooseSoundCook.Run(source, file.ContentPath, settings);
            diagnostics.AddRange(result.Diagnostics);

            if (result.Cooked is { } bytes && TryWrite(target, bytes, diagnostics)) cooked++;
        }

        return new LooseSoundFolderResult(cooked, upToDate, diagnostics);
    }

    private static string LabelFileOf(string contentRoot, string soundPath) =>
        Path.Combine(contentRoot, MarkerLabelFile.PathFor(soundPath).Replace('/', Path.DirectorySeparatorChar));

    private static bool IsUpToDate(string cooked, string sound, string labels)
    {
        if (!File.Exists(cooked)) return false;

        DateTime cookedAt = File.GetLastWriteTimeUtc(cooked);
        if (File.GetLastWriteTimeUtc(sound) > cookedAt) return false;

        return !File.Exists(labels) || File.GetLastWriteTimeUtc(labels) <= cookedAt;
    }

    private static bool TryWrite(string path, byte[] bytes, List<CookDiagnostic> diagnostics)
    {
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
            AtomicOutput.Write(path, stream => stream.Write(bytes));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.OutputNotWritable, $"Could not write '{path}': {ex.Message}", path));
            return false;
        }
    }
}
