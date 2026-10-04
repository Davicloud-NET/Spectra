using SpectraEngine.Core.Assets.Sources;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Spectra.Kitchen.Rules;

// A rule's view of the content root as an IContentSource, so a rule can call the
// engine's resolution helpers (ImageContentPath.Resolve) instead of restating them.
// Reads and probes go through the context, so dependency recording still holds.
internal sealed class RuleContentSource : IContentSource
{
    private readonly IRuleContext _context;

    public RuleContentSource(IRuleContext context) => _context = context;

    // Never mounted into a stack, so the priority is unused.
    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public bool TryOpen(string path, [NotNullWhen(true)] out ContentBlob? blob)
    {
        // Probe first: Read throws on a miss, and a source has to answer false.
        if (!_context.Probe(path))
        {
            blob = null;
            return false;
        }

        blob = ContentBlob.CopyOf(_context.Read(path));
        return true;
    }

    /// <inheritdoc/>
    public bool Exists(string path) => _context.Probe(path);

    /// <inheritdoc/>
    public bool TryGetWatchPath(string path, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;
        return false;
    }

    /// <inheritdoc/>
    // Lists nothing: enumerating would make every file seen a dependency.
    public void TryEnumerate(string prefix, string extension, List<string> results)
    {
        ArgumentNullException.ThrowIfNull(results);
    }
}
