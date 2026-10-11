using SpectraEngine.Core.Assets.Sources;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// Reads a script through the content sources, so a loose file and a pack
/// give the same text.
/// </summary>
/// <param name="content">The mounted content, or null when there is none and every read misses.</param>
public sealed class ContentScriptSource(IContentSource? content) : IScriptSource
{
    /// <inheritdoc/>
    public byte[]? Read(string path)
    {
        if (content is null || !content.TryOpen(path, out ContentBlob? blob))
            return null;

        using (blob)
            return blob.Span.ToArray();
    }
}
