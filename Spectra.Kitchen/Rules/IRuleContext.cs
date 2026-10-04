using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Everything a rule may do: read or probe an input, emit an output, report a
/// diagnostic. Every access is recorded as a dependency, misses included. Paths are
/// content-relative and normalised.
/// </summary>
public interface IRuleContext
{
    /// <summary>The content-relative path this rule was asked to cook.</summary>
    string SourcePath { get; }

    // Only settings a rule can declare in IRule.SettingsRead are exposed here.
    // Handing over the whole CookSettings would let a rule read one that is not
    // in its cache key.

    /// <summary>The profile the cook is running under.</summary>
    CookProfile Profile { get; }

    /// <summary>The graphics backends this cook was asked for, in the order given.</summary>
    IReadOnlyList<GraphicsBackend> Targets { get; }

    /// <summary>The sample rate every cooked sound is resampled to.</summary>
    int AudioSampleRate { get; }

    /// <summary>Whether to keep every brush's authored planes in the compiled map.</summary>
    bool KeepBrushSource { get; }

    /// <summary>
    /// The bytes at <paramref name="contentPath"/>, recording the read. A miss is
    /// recorded before it throws.
    /// </summary>
    /// <exception cref="RuleInputMissingException">There is nothing at that path.</exception>
    byte[] Read(string contentPath);

    /// <summary>Whether anything exists at <paramref name="contentPath"/>, recording the answer either way.</summary>
    bool Probe(string contentPath);

    /// <summary>
    /// Every file under <paramref name="contentPath"/>, recursively, as
    /// content-relative paths in ordinal order. An absent directory lists nothing.
    /// The listing itself is not a recorded dependency: a file added to the folder
    /// later does not invalidate a cached result.
    /// </summary>
    IReadOnlyList<string> ListFiles(string contentPath);

    /// <summary>
    /// Emits one cooked output at <paramref name="outputPath"/>, which becomes a
    /// pack entry or a file in the loose tree.
    /// </summary>
    /// <param name="outputPath">Content-relative path the engine will resolve it by.</param>
    /// <param name="payload">The cooked bytes, copied.</param>
    /// <param name="kind">Routing hint carried by the pack entry.</param>
    void Emit(string outputPath, ReadOnlySpan<byte> payload, PackEntryKind kind = PackEntryKind.Raw);

    /// <summary>Records and copies a source without materializing it when the host supports file payloads.</summary>
    void Copy(string sourcePath, string outputPath, PackEntryKind kind = PackEntryKind.Raw) => Emit(outputPath, Read(sourcePath), kind);

    /// <summary>Reports a diagnostic against this rule. Buffered and flushed in rule order.</summary>
    void Report(CookDiagnostic diagnostic);
}
