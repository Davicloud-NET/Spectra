using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Packs;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Audio;
using System;
using System.Collections.Generic;
using System.IO;

namespace Spectra.Kitchen.Cooking;

/// <summary>
/// Cooks one sound on its own, with no project and no pack, by running the
/// rule a project cook runs. The bytes are the ones that cook would put in the
/// pack. For the editor, which plays loose files, and for a build that cooks a
/// folder of sounds.
/// </summary>
public static class LooseSoundCook
{
    // A rule keeps no state between runs, so one serves every thread.
    private static readonly AudioRule Rule = new();

    private static readonly CookSettings DefaultSettings = new();

    /// <summary>
    /// The sound under <paramref name="contentRoot"/> that cooks to
    /// <paramref name="cookedPath"/>, as a content path. Null when there is
    /// none, or when the path is not a cooked sound's.
    /// </summary>
    public static string? FindSource(string contentRoot, string cookedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(cookedPath);

        if (!AudioContentPath.IsCooked(cookedPath)) return null;

        foreach (string extension in AudioRule.SourceExtensions)
        {
            string candidate = Path.ChangeExtension(cookedPath, extension);

            try
            {
                if (File.Exists(ContentRoot.ResolveAbsolute(contentRoot, candidate))) return candidate;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                // Not a path inside the content root, so nothing cooks to it.
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Cooks the sound at <paramref name="soundPath"/> under
    /// <paramref name="contentRoot"/>. A sound the cook refuses comes back
    /// with no bytes and the reason among the diagnostics.
    /// </summary>
    /// <param name="settings">
    /// The project rate and strictness to cook with. Null cooks with a
    /// project cook's defaults.
    /// </param>
    public static LooseSoundResult Run(string contentRoot, string soundPath, CookSettings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(soundPath);

        settings ??= DefaultSettings;
        settings.Validate();

        var context = new RuleContext(
            contentRoot, soundPath, settings.Profile, audioSampleRate: settings.AudioSampleRate);

        CookDiagnostic? failure = RuleRun.Cook(Rule, context, context.ResolveFullPath(context.SourcePath));

        var log = new CookDiagnosticLog(settings.Strict);
        if (failure is not null) log.Add(failure);
        log.AddRange(context.Diagnostics);

        byte[]? cooked = !log.Failed && context.Emissions.Count == 1 ? context.Emissions[0].Payload : null;

        return new LooseSoundResult(
            cooked,
            log.Entries,
            context.Dependencies,
            KeyOf(settings, context.Dependencies),
            IsRepeatable: failure is null);
    }

    /// <summary>
    /// The key a cook would have now, over the files an earlier one touched.
    /// It equals that cook's <see cref="LooseSoundResult.Key"/> until one of
    /// them changes, appears or goes, or the cook itself changes.
    /// </summary>
    /// <param name="recorded">The earlier cook's <see cref="LooseSoundResult.Dependencies"/>.</param>
    public static UInt128 CurrentKey(
        string contentRoot, IReadOnlyList<RuleDependency> recorded, CookSettings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(recorded);

        return KeyOf(
            settings ?? DefaultSettings,
            RestatedDependencies.Of(Path.GetFullPath(contentRoot), recorded, TryHash));
    }

    private static UInt128 KeyOf(CookSettings settings, IReadOnlyList<RuleDependency> dependencies) =>
        CookCacheKey.Compute(Rule.Kind, Rule.Version, Rule.SettingsRead, settings, dependencies);

    // No stat cache: one sound is cheap to read and hash.
    private static bool TryHash(string contentPath, string fullPath, out UInt128 hash)
    {
        try
        {
            hash = PackPayload.FromFile(fullPath).Hash;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            hash = UInt128.Zero;
            return false;
        }
    }
}
