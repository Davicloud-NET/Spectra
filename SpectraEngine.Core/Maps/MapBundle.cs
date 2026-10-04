using System;
using System.IO;

namespace SpectraEngine.Core.Maps;

/// <summary>
/// Reads and writes a <c>.smap</c> bundle: a folder of text, not a file.
/// A save writes only files whose bytes changed and never touches a file it does not own.
/// </summary>
public static class MapBundle
{
    /// <summary>Reads the scene document out of a bundle directory.</summary>
    public static MapDocument Load(string bundlePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);

        string document = DocumentPath(bundlePath);
        if (!File.Exists(document))
        {
            throw new FileNotFoundException(
                $"'{bundlePath}' is not a map bundle: it has no {MapFormat.DocumentFileName}.", document);
        }

        return MapReader.Read(File.ReadAllBytes(document));
    }

    /// <summary>Writes the scene document into a bundle directory, creating it if needed.</summary>
    /// <returns>False when the file already held the same bytes and was left alone.</returns>
    public static bool Save(string bundlePath, MapDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        ArgumentNullException.ThrowIfNull(document);

        Directory.CreateDirectory(bundlePath);
        return WriteIfChanged(DocumentPath(bundlePath), MapWriter.Write(document));
    }

    /// <summary>The bundle-relative document path.</summary>
    public static string DocumentPath(string bundlePath) =>
        Path.Combine(bundlePath, MapFormat.DocumentFileName);

    /// <summary>Whether a directory looks like a map bundle.</summary>
    public static bool IsBundle(string path) =>
        Directory.Exists(path) && File.Exists(DocumentPath(path));

    // The read-back keeps the mtime still on an unedited save, so no watcher,
    // cook or git status sees a change.
    private static bool WriteIfChanged(string path, byte[] content)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            return false;

        // Temp file on the same volume, then rename. A crash mid-write cannot
        // leave a truncated document.
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, overwrite: true);
        return true;
    }
}
