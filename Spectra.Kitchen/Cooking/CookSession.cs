using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Packs;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Projects;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Threading;

namespace Spectra.Kitchen.Cooking;

/// <summary>
/// One cook of one project: walk the content, run a rule per asset, write the
/// pack. A failed cook writes no pack.
/// </summary>
// Workers only fill one outcome slot per work item. Diagnostics, pack entries,
// loose files, counters and manifest rows are applied afterwards on the calling
// thread in work-list order, so no cooked byte depends on scheduling.
public sealed class CookSession
{
    private readonly ProjectLayout _layout;
    private readonly CookSettings _settings;
    private readonly CookRuleSet _rules;

    /// <summary>The extension a cooked pack is written with.</summary>
    public const string PackExtension = PackFormat.FileExtension;

    /// <summary>Creates a session over an opened project.</summary>
    public CookSession(ProjectLayout layout, CookSettings settings)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(settings);

        settings.Validate();
        _layout = layout;
        _settings = settings;
        _rules = new CookRuleSet(layout.Project.LanguageOrDefault);
    }

    /// <summary>Where output goes: <c>-o</c> if it was given, else the project's <c>cooked/</c>.</summary>
    public string OutputDirectory => _settings.OutputPath ?? _layout.CookedPath;

    /// <summary>
    /// Where the cook cache lives: <c>.spectra-cook/</c> at the project root.
    /// Not moved by <c>-o</c>.
    /// </summary>
    public string CacheDirectory => Path.Combine(_layout.Root, CookCache.DirectoryName);

    /// <summary>Runs the cook.</summary>
    public CookResult Run() => Run(CancellationToken.None);

    public CookResult Run(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new CookDiagnosticLog(_settings.Strict);
        var assets = new List<CookedAsset>();
        var emitted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var writer = new PackWriter();
        var looseFiles = new List<RuleEmission>();

        long payloadBytes = 0;
        int entryCount = 0;

        string contentRoot = _layout.AssetsPath;
        if (!Directory.Exists(contentRoot))
        {
            diagnostics.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.ContentRootMissing,
                $"The project has no '{ProjectFormat.AssetsFolder}' folder at '{contentRoot}', so there is " +
                "nothing to cook.",
                _layout.ManifestPath));

            return Finish(assets, diagnostics, null, null, 0, 0, 0);
        }

        CookCache? cache = OpenCache(diagnostics);
        using var spool = new PayloadSpool(cache?.PayloadStore);

        // Build the whole work list in walk order before anything runs. All
        // output ordering is an index into it. Rules are resolved here so
        // CookRuleSet stays on one thread.
        IReadOnlyList<ContentFile> content = ContentWalker.Walk(contentRoot);
        IReadOnlyList<string> maps = _layout.DiscoverMaps();

        var work = new WorkItem[content.Count + maps.Count];
        var cookedPaths = new List<string>(work.Length);

        for (int i = 0; i < content.Count; i++)
        {
            work[i] = new WorkItem(contentRoot, content[i], _rules.Resolve(content[i].ContentPath));
            cookedPaths.Add(content[i].ContentPath);
        }

        // Maps live beside Assets/, so their paths are relative to the project root.
        string projectRoot = Path.GetFullPath(_layout.Root);
        for (int i = 0; i < maps.Count; i++)
        {
            var bundle = new ContentFile(maps[i], _layout.Resolve(maps[i]));
            work[content.Count + i] = new WorkItem(projectRoot, bundle, _rules.ResolveMap());
            cookedPaths.Add(maps[i]);
        }

        int workers = work.Length == 0 ? 0 : Math.Clamp(_settings.Jobs, 1, work.Length);
        var outcomes = new RuleOutcome[work.Length];

        // One level: no rule depends on another rule's output yet. A level is a
        // range of the work list, so a real dependency order only needs a sort.
        RunLevel(cache, spool.Store, work, outcomes, 0, work.Length, workers, cancellationToken);

        for (int i = 0; i < work.Length; i++)
        {
            WorkItem item = work[i];
            RuleOutcome outcome = outcomes[i];

            if (outcome.Failure is not null) diagnostics.Add(outcome.Failure);

            // The log applies CookGate. Severity is decided there, not here.
            diagnostics.AddRange(outcome.Reports);

            var outputs = new List<CookedOutput>(outcome.Emissions.Count);
            foreach (RuleEmission emission in outcome.Emissions)
            {
                if (emitted.TryGetValue(emission.Path, out string? firstOwner))
                {
                    diagnostics.Add(CookDiagnostic.Error(
                        CookDiagnosticCodes.PackEntryCollision,
                        $"'{emission.Path}' is emitted by both '{firstOwner}' and '{item.File.ContentPath}'. " +
                        "One content path is one asset.",
                        item.File.FullPath));
                    continue;
                }

                emitted.Add(emission.Path, item.File.ContentPath);

                if (_settings.Loose) looseFiles.Add(emission);
                else writer.Add(emission.Path, emission.Kind, emission.Content);

                outputs.Add(new CookedOutput(
                    emission.Path,
                    PackAssetId.FromNormalized(emission.Path),
                    emission.Content.Hash,
                    emission.Content.Length));

                payloadBytes += emission.Content.Length;
                entryCount++;
            }

            assets.Add(new CookedAsset(
                item.File.ContentPath, item.Rule.Kind, item.Rule.Version, [.. outcome.Dependencies], outputs)
            {
                FromCache = outcome.FromCache,
            });
        }

        // A subtitle file that is missing has no rule to run for it.
        diagnostics.AddRange(SubtitleCoverage.Check(content, _layout.Project.LanguageOrDefault));

        // Save even when the cook failed, so one broken file does not re-cook
        // everything else on the next attempt.
        CloseCache(cache, cookedPaths, diagnostics);

        if (diagnostics.Failed)
            return Finish(assets, diagnostics, cache, null, 0, 0, workers);

        string? output = _settings.Loose
            ? WriteLoose(looseFiles, diagnostics, cancellationToken)
            : WritePack(writer, diagnostics, cancellationToken);

        if (output is null || diagnostics.Failed)
            return Finish(assets, diagnostics, cache, null, 0, 0, workers);

        WriteManifest(assets, diagnostics);

        return Finish(assets, diagnostics, cache, output, entryCount, payloadBytes, workers);
    }

    private readonly record struct WorkItem(string ContentRoot, ContentFile File, IRule Rule);

    // The only thing that crosses back from a worker.
    private sealed record RuleOutcome(
        bool FromCache,
        IReadOnlyList<RuleDependency> Dependencies,
        IReadOnlyList<RuleEmission> Emissions,
        CookDiagnostic? Failure,
        IReadOnlyList<CookDiagnostic> Reports);

    // The only parallel part. Writes one outcome slot per work item, nothing else.
    private void RunLevel(
        CookCache? cache,
        ContentStore payloadStore,
        WorkItem[] work,
        RuleOutcome[] outcomes,
        int start,
        int count,
        int workers, CancellationToken cancellationToken)
    {
        if (count == 0) return;

        // -j1 takes this path too. A separate serial branch would be a second
        // implementation to keep in step.
        var options = new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellationToken };

        try
        {
            Parallel.For(start, start + count, options, i => outcomes[i] = RunOne(cache, payloadStore, work[i], cancellationToken));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
        {
            // Unwrap, so a rule bug surfaces with its own message and stack.
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }
    }

    // Runs on a worker. The cache is the only shared state it touches.
    private RuleOutcome RunOne(CookCache? cache, ContentStore payloadStore, WorkItem item, CancellationToken cancellationToken)
    {
        if (cache is not null &&
            cache.TryReplay(item.ContentRoot, item.File.ContentPath, item.Rule, _settings, out CachedRun? replay))
        {
            // No reports: a run that reported anything is never recorded.
            return new RuleOutcome(true, replay.Dependencies, replay.Emissions, null, []);
        }

        var context = new RuleContext(
            item.ContentRoot,
            item.File.ContentPath,
            _settings.Profile,
            _settings.Targets,
            _settings.AudioSampleRate,
            _settings.KeepBrushSource, payloadStore, cancellationToken);

        CookDiagnostic? failure = RuleRun.Cook(item.Rule, context, item.File.FullPath);

        // Don't record a run that failed or reported. The cache keeps no
        // diagnostics, so a later hit would drop the message.
        if (cache is not null && failure is null && context.Diagnostics.Count == 0)
        {
            cache.Record(
                item.File.ContentPath, item.Rule, _settings, context.Dependencies, context.Emissions);
        }

        return new RuleOutcome(
            false, context.Dependencies, context.Emissions, failure, context.Diagnostics);
    }

    // A cache that cannot be opened means a clean cook, not a failed one.
    private CookCache? OpenCache(CookDiagnosticLog diagnostics)
    {
        if (!_settings.UseCache) return null;

        CookCache cache;
        try
        {
            cache = new CookCache(CacheDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CookDiagnostic.Warning(
                CookDiagnosticCodes.CacheNotWritable,
                $"The cook cache at '{CacheDirectory}' could not be opened, so this is a clean cook: {ex.Message}"));

            return null;
        }

        if (cache.DiscardedReason is not null)
        {
            diagnostics.Add(CookDiagnostic.Info(
                CookDiagnosticCodes.CacheDiscarded,
                $"The cook cache at '{CacheDirectory}' could not be read and was discarded, so this is a clean " +
                $"cook: {cache.DiscardedReason}"));
        }

        return cache;
    }

    private void CloseCache(CookCache? cache, IReadOnlyCollection<string> cooked, CookDiagnosticLog diagnostics)
    {
        if (cache is null) return;

        cache.RetainOnly(cooked);

        try
        {
            cache.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CookDiagnostic.Warning(
                CookDiagnosticCodes.CacheNotWritable,
                $"The cook cache at '{CacheDirectory}' could not be written, so the next cook will not be " +
                $"incremental: {ex.Message}"));
        }
    }

    private string? WritePack(PackWriter writer, CookDiagnosticLog diagnostics, CancellationToken cancellationToken)
    {
        // Named after the manifest file. The display name may hold characters
        // a filesystem refuses.
        string packPath = Path.Combine(
            OutputDirectory, Path.GetFileNameWithoutExtension(_layout.ManifestPath) + PackExtension);

        try
        {
            Directory.CreateDirectory(OutputDirectory);
            writer.WriteToFile(packPath, cancellationToken);
            return packPath;
        }
        catch (InvalidOperationException ex)
        {
            // Two paths hashing to one id. A content problem, not an I/O one.
            diagnostics.Add(CookDiagnostic.Error(CookDiagnosticCodes.PackEntryCollision, ex.Message));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.PackWriteFailed, $"Could not write '{packPath}': {ex.Message}"));
            return null;
        }
    }

    private string? WriteLoose(List<RuleEmission> emissions, CookDiagnosticLog diagnostics, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            foreach (RuleEmission emission in emissions)
            {
                string full = Path.Combine(
                    OutputDirectory, emission.Path.Replace('/', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                AtomicOutput.Write(full, stream => emission.Content.CopyTo(stream, cancellationToken), cancellationToken);
            }

            return OutputDirectory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.OutputNotWritable,
                $"Could not write the loose cook tree at '{OutputDirectory}': {ex.Message}"));
            return null;
        }
    }

    private void WriteManifest(List<CookedAsset> assets, CookDiagnosticLog diagnostics)
    {
        if (_settings.ManifestPath is null) return;

        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(_settings.ManifestPath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            AtomicOutput.Write(_settings.ManifestPath, stream =>
                stream.Write(CookManifest.Write(_layout.Project.Name, _settings.Profile, assets)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CookDiagnostic.Error(
                CookDiagnosticCodes.OutputNotWritable,
                $"Could not write the cook manifest '{_settings.ManifestPath}': {ex.Message}"));
        }
    }

    private static CookResult Finish(
        List<CookedAsset> assets,
        CookDiagnosticLog diagnostics,
        CookCache? cache,
        string? output,
        int entryCount,
        long payloadBytes,
        int workers)
    {
        return new CookResult
        {
            Assets = assets,
            Diagnostics = diagnostics.Entries,
            OutputPath = output,
            EntryCount = entryCount,
            PayloadBytes = payloadBytes,
            ErrorCount = diagnostics.ErrorCount,
            WarningCount = diagnostics.WarningCount,
            CacheHits = cache?.Hits ?? 0,
            CacheMisses = cache?.Misses ?? 0,
            Workers = workers,
        };
    }
}
