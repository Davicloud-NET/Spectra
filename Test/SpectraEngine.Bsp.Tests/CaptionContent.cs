using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio.Captions;

namespace SpectraEngine.Bsp.Tests;

// A folder of loose content for caption and subtitle files, and a library
// that reads it.
internal sealed class CaptionContent : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SpectraCaptionTests", Guid.NewGuid().ToString("N"));

    public CaptionContent()
    {
        Directory.CreateDirectory(_root);
        Source = new LooseFileSource(NullLogger.Instance, _root);
    }

    public LooseFileSource Source { get; }

    // What the libraries made here logged.
    public CapturingLogger Log { get; } = new();

    public CaptionLibrary Library(string projectLanguage = "en") => new(Source, projectLanguage, Log);

    public void Write(string contentPath, string text)
    {
        string full = Path.Combine(_root, contentPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full).ShouldNotBeNull());
        File.WriteAllText(full, text);
    }

    public void Delete(string contentPath) =>
        File.Delete(Path.Combine(_root, contentPath.Replace('/', Path.DirectorySeparatorChar)));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
