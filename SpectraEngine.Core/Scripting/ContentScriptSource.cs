using SpectraEngine.Core.Assets.Sources;
using System;
using System.IO;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// Reads a script through the content sources. A loose file is the script's
/// text and a pack holds it behind a header, and both give the same text.
/// </summary>
/// <param name="content">The mounted content, or null when there is none and every read misses.</param>
public sealed class ContentScriptSource(IContentSource? content) : IScriptSource
{
    /// <inheritdoc/>
    public byte[]? Read(string path, out string? problem)
    {
        problem = null;
        if (content is null || !content.TryOpen(path, out ContentBlob? blob))
            return null;

        using (blob)
        {
            try
            {
                return PackedScript.TextOf(blob.Span, path).ToArray();
            }
            catch (InvalidDataException e)
            {
                problem = e.Message;
                return null;
            }
        }
    }
}
