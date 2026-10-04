using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Rules;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace Spectra.Kitchen.Cache;

/// <summary>What a cached rule run produced, ready to be emitted again.</summary>
/// <param name="Dependencies">What the rule touched when it ran, misses included.</param>
/// <param name="Emissions">Its outputs, payloads read back from the store.</param>
public sealed record CachedRun(
    IReadOnlyList<RuleDependency> Dependencies,
    IReadOnlyList<RuleEmission> Emissions);

/// <summary>
/// The incremental cook: a dependency graph, a content-addressed payload
/// store, and a stat cache that saves re-hashing.
/// </summary>
// A hit is decided by asking the recorded dependencies again against today's
// filesystem and comparing keys. A probe that missed and now finds a file
// changes the key, so the rule re-runs.
// Any failure here is a miss. A rule that reported a diagnostic is never
// cached, since a replay could not repeat what it said.
// Called from N workers. The graph and stat cache lock themselves, the store
// relies on temp file plus rename. One work item per source path, so no two
// workers touch one record.
public sealed class CookCache
{
    /// <summary>The folder a project's cook cache lives in, at the project root.</summary>
    public const string DirectoryName = ".spectra-cook";

    private const string GraphFileName = "graph.bin";
    private const string StatFileName = "stat.bin";

    private readonly string _root;
    private readonly CookGraph _graph;
    private readonly StatCache _stat;
    private readonly ContentStore _store;
    internal ContentStore PayloadStore => _store;

    private int _hits;
    private int _misses;

    /// <summary>Opens the cache rooted at <paramref name="root"/>, reading what is there.</summary>
    public CookCache(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = Path.GetFullPath(root);
        _graph = CookGraph.Load(Path.Combine(_root, GraphFileName));
        _stat = StatCache.Load(Path.Combine(_root, StatFileName));
        _store = new ContentStore(_root);
    }

    /// <summary>The cache folder.</summary>
    public string Root => _root;

    /// <summary>Rules answered from the cache.</summary>
    public int Hits => Volatile.Read(ref _hits);

    /// <summary>Rules that had to run.</summary>
    public int Misses => Volatile.Read(ref _misses);

    /// <summary>Inputs whose hash was answered without reading them.</summary>
    public int StatShortCircuits => _stat.ShortCircuits;

    /// <summary>Inputs that had to be read and hashed.</summary>
    public int StatRehashes => _stat.Rehashes;

    /// <summary>Why a graph file was discarded, or null.</summary>
    public string? DiscardedReason => _graph.DiscardedReason;

    /// <summary>
    /// Answers <paramref name="sourcePath"/> from the cache, or reports a miss.
    /// </summary>
    /// <param name="contentRoot">Absolute path of the project's content root.</param>
    /// <param name="sourcePath">Normalised content-relative path of the asset.</param>
    public bool TryReplay(
        string contentRoot,
        string sourcePath,
        IRule rule,
        CookSettings settings,
        [NotNullWhen(true)] out CachedRun? run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(settings);

        run = null;

        if (!_graph.TryGet(sourcePath, out CookGraphRecord record))
        {
            Interlocked.Increment(ref _misses);
            return false;
        }

        IReadOnlyList<RuleDependency> restated = Restate(contentRoot, record.Dependencies);
        UInt128 key = CookCacheKey.Compute(
            rule.Kind, rule.Version, rule.SettingsRead, settings, restated);

        CookGeneration? generation = null;
        for (int i = 0; i < record.Generations.Count; i++)
        {
            if (record.Generations[i].Key != key) continue;

            generation = record.Generations[i];
            break;
        }

        if (generation is null)
        {
            Interlocked.Increment(ref _misses);
            return false;
        }

        var emissions = new List<RuleEmission>(generation.Outputs.Count);
        for (int i = 0; i < generation.Outputs.Count; i++)
        {
            CachedOutput output = generation.Outputs[i];
            if (!_store.TryGetPayload(output.ContentHash, out var payload) || payload.Length != output.Length)
            {
                // Payload gone from the store: miss. The re-run puts it back.
                Interlocked.Increment(ref _misses);
                return false;
            }

            emissions.Add(new RuleEmission(output.Path, output.Kind, payload));
        }

        Interlocked.Increment(ref _hits);

        // Restated, not recorded: it describes the filesystem the manifest is
        // written against.
        run = new CachedRun(restated, emissions);
        return true;
    }

    /// <summary>Records one rule run so a later cook can replay it.</summary>
    public void Record(
        string sourcePath,
        IRule rule,
        CookSettings settings,
        IReadOnlyList<RuleDependency> dependencies,
        IReadOnlyList<RuleEmission> emissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(emissions);

        var outputs = new CachedOutput[emissions.Count];
        for (int i = 0; i < emissions.Count; i++)
        {
            RuleEmission emission = emissions[i];
            var payload = _store.PutPayload(emission.Content);
            outputs[i] = new CachedOutput(emission.Path, emission.Kind, payload.Hash, payload.Length);
        }

        UInt128 key = CookCacheKey.Compute(
            rule.Kind, rule.Version, rule.SettingsRead, settings, dependencies);

        _graph.Set(sourcePath, dependencies, key, outputs);
    }

    /// <summary>Drops records for assets that are no longer in the project.</summary>
    public void RetainOnly(IReadOnlyCollection<string> liveSourcePaths) => _graph.RetainOnly(liveSourcePaths);

    /// <summary>Persists the graph and the stat cache, if either moved.</summary>
    public void Save()
    {
        if (_graph.IsDirty) _graph.Save(Path.Combine(_root, GraphFileName));
        if (_stat.IsDirty) _stat.Save(Path.Combine(_root, StatFileName));
    }

    // Keep the recorded order, or the restated key cannot match the recorded one.
    private IReadOnlyList<RuleDependency> Restate(
        string contentRoot, IReadOnlyList<RuleDependency> recorded)
    {
        var restated = new RuleDependency[recorded.Count];
        for (int i = 0; i < recorded.Count; i++)
        {
            RuleDependency dependency = recorded[i];
            string full = Path.Combine(
                contentRoot, dependency.Path.Replace('/', Path.DirectorySeparatorChar));

            restated[i] = dependency.Kind switch
            {
                // Contents are in the key, so re-hash (usually answered by the stat cache).
                RuleDependencyKind.Read => _stat.TryGetHash(dependency.Path, full, out UInt128 hash)
                    ? new RuleDependency(dependency.Path, RuleDependencyKind.Read, hash)
                    : new RuleDependency(dependency.Path, RuleDependencyKind.ProbeMissing, UInt128.Zero),

                // A probe only asked whether the file exists. No hash.
                _ => File.Exists(full)
                    ? new RuleDependency(dependency.Path, RuleDependencyKind.ProbeFound, UInt128.Zero)
                    : new RuleDependency(dependency.Path, RuleDependencyKind.ProbeMissing, UInt128.Zero),
            };
        }

        return restated;
    }
}
