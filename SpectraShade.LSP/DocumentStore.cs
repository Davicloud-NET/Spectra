using OmniSharp.Extensions.LanguageServer.Protocol;

namespace SpectraShade.LSP;

// Text of the open documents. The editor sends full text on each change.
internal sealed class DocumentStore
{
    private readonly Dictionary<DocumentUri, string> _documents = [];

    public void Update(DocumentUri uri, string content)
    {
        _documents[uri] = content;
    }

    public void Remove(DocumentUri uri)
    {
        _documents.Remove(uri);
    }

    public string? Get(DocumentUri uri)
    {
        return _documents.GetValueOrDefault(uri);
    }
}
