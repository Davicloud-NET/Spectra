using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.IO;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// Reads a script through the content sources. A loose file is the script's
/// text and a pack holds it behind a header, and both give the same text.
/// </summary>
/// <param name="content">The mounted content, or null when there is none and every read misses.</param>
/// <param name="logger">Where a packed script this build cannot read is named. Null says nothing.</param>
public sealed class ContentScriptSource(IContentSource? content, ILogger? logger = null) : IScriptSource
{
    /// <inheritdoc/>
    public byte[]? Read(string path)
    {
        if (content is null || !content.TryOpen(path, out ContentBlob? blob))
            return null;

        using (blob)
        {
            ReadOnlySpan<byte> file = blob.Span;
            if (!PackedScript.IsPacked(file))
                return file.ToArray();

            try
            {
                return PackedScript.SourceOf(file, path).ToArray();
            }
            catch (InvalidDataException e)
            {
                logger?.LogError("{Why} The script does not start.", e.Message);
                return null;
            }
        }
    }
}
