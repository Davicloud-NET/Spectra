using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Packs;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Threading;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// The recording <see cref="IRuleContext"/>. One per rule run, never shared: it holds
/// that run's dependencies, outputs and diagnostics.
/// </summary>
// Reads the filesystem directly, not an IContentSource: a source stack could have
// a pack layered in, and a cook must not read its own output.
public sealed class RuleContext : IRuleContext
{
    private readonly string _contentRoot;
    private readonly List<RuleDependency> _dependencies = [];
    private readonly Dictionary<string, int> _dependencyIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RuleEmission> _emissions = [];
    private readonly List<CookDiagnostic> _diagnostics = [];
    private readonly ContentStore? _emissionStore;
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// Creates the context for one rule run over <paramref name="sourcePath"/>.
    /// Optional settings default to what <see cref="CookSettings"/> defaults to.
    /// </summary>
    /// <param name="contentRoot">Absolute path of the project's content root.</param>
    /// <param name="sourcePath">Content-relative path of the asset being cooked.</param>
    public RuleContext(
        string contentRoot,
        string sourcePath,
        CookProfile profile,
        IReadOnlyList<GraphicsBackend>? targets = null,
        int audioSampleRate = CookSettings.DefaultAudioSampleRate,
        bool keepBrushSource = false,
        ContentStore? emissionStore = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        _contentRoot = Path.GetFullPath(contentRoot);
        SourcePath = ContentRoot.NormalizeRelativePath(sourcePath);
        Profile = profile;
        Targets = targets ?? CookSettings.DefaultTargets;
        AudioSampleRate = audioSampleRate;
        KeepBrushSource = keepBrushSource;
        _emissionStore = emissionStore;
        _cancellationToken = cancellationToken;
    }

    /// <inheritdoc/>
    public string SourcePath { get; }

    /// <inheritdoc/>
    public CookProfile Profile { get; }

    /// <inheritdoc/>
    public IReadOnlyList<GraphicsBackend> Targets { get; }

    /// <inheritdoc/>
    public int AudioSampleRate { get; }

    /// <inheritdoc/>
    public bool KeepBrushSource { get; }

    /// <summary>Every path this rule touched, in first-access order.</summary>
    public IReadOnlyList<RuleDependency> Dependencies => _dependencies;

    /// <summary>What this rule emitted, in emission order.</summary>
    public IReadOnlyList<RuleEmission> Emissions => _emissions;

    /// <summary>What this rule reported, in report order.</summary>
    public IReadOnlyList<CookDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Absolute path of <paramref name="contentPath"/> under this cook's content root.</summary>
    public string ResolveFullPath(string contentPath) => ToFullPath(Normalize(contentPath));

    /// <inheritdoc/>
    public byte[] Read(string contentPath)
    {
        string normalized = Normalize(contentPath);
        string full = ToFullPath(normalized);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(full);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Record the miss before throwing, so adding the file later re-runs this rule.
            Record(normalized, RuleDependencyKind.ProbeMissing, UInt128.Zero);
            throw new RuleInputMissingException(normalized, SourcePath);
        }

        Record(normalized, RuleDependencyKind.Read, XxHash128.HashToUInt128(bytes));
        return bytes;
    }

    /// <inheritdoc/>
    public bool Probe(string contentPath)
    {
        string normalized = Normalize(contentPath);
        bool found = File.Exists(ToFullPath(normalized));
        Record(normalized, found ? RuleDependencyKind.ProbeFound : RuleDependencyKind.ProbeMissing, UInt128.Zero);
        return found;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ListFiles(string contentPath)
    {
        string normalized = Normalize(contentPath);
        string full = ToFullPath(normalized);

        if (!Directory.Exists(full)) return [];

        var found = new List<string>();
        foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(_contentRoot, file);

            try
            {
                found.Add(ContentRoot.NormalizeRelativePath(relative));
            }
            catch (ArgumentException)
            {
                // Skip a name that will not normalise, as ContentWalker does.
            }
        }

        // EnumerateFiles has no documented order.
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <inheritdoc/>
    public void Emit(string outputPath, ReadOnlySpan<byte> payload, PackEntryKind kind = PackEntryKind.Raw)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        PackPayload stored = _emissionStore is null ? PackPayload.FromBytes(payload.ToArray()) : _emissionStore.PutPayload(payload);
        _emissions.Add(new RuleEmission(Normalize(outputPath), kind, stored));
    }

    public void Copy(string sourcePath, string outputPath, PackEntryKind kind = PackEntryKind.Raw)
    {
        if (_emissionStore is null) { Emit(outputPath, Read(sourcePath), kind); return; }
        _cancellationToken.ThrowIfCancellationRequested();
        string normalized = Normalize(sourcePath);
        PackPayload payload;
        try { payload = _emissionStore.PutFile(ToFullPath(normalized), _cancellationToken); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Record(normalized, RuleDependencyKind.ProbeMissing, UInt128.Zero);
            throw new RuleInputMissingException(normalized, SourcePath);
        }
        Record(normalized, RuleDependencyKind.Read, payload.Hash);
        _emissions.Add(new RuleEmission(Normalize(outputPath), kind, payload));
        _cancellationToken.ThrowIfCancellationRequested();
    }

    /// <inheritdoc/>
    public void Report(CookDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _diagnostics.Add(diagnostic);
    }

    private static string Normalize(string contentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);
        return ContentRoot.NormalizeRelativePath(contentPath);
    }

    private string ToFullPath(string normalized) =>
        Path.Combine(_contentRoot, normalized.Replace('/', Path.DirectorySeparatorChar));

    private void Record(string normalized, RuleDependencyKind kind, UInt128 hash)
    {
        if (!_dependencyIndex.TryGetValue(normalized, out int at))
        {
            _dependencyIndex[normalized] = _dependencies.Count;
            _dependencies.Add(new RuleDependency(normalized, kind, hash));
            return;
        }

        // Strongest observation wins. The slot keeps its first-access position,
        // because the cook key hashes dependencies in order.
        if (Strength(kind) > Strength(_dependencies[at].Kind))
            _dependencies[at] = new RuleDependency(normalized, kind, hash);
    }

    private static int Strength(RuleDependencyKind kind) => kind switch
    {
        RuleDependencyKind.Read => 2,
        RuleDependencyKind.ProbeFound => 1,
        _ => 0,
    };
}
