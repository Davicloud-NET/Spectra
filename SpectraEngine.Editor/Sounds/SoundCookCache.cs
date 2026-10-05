using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Assets;
using System;
using System.IO;
using System.IO.Hashing;
using System.Text;

namespace SpectraEngine.Editor.Sounds;

// Where the editor keeps the sounds it has cooked, one file per sound, so the
// next start does not cook them again. Writing a sound replaces its file.
// Any thread: a write is a temp file and a rename.
internal sealed class SoundCookCache
{
    private const string EntryExtension = ".cache";

    public SoundCookCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
    }

    public string Directory { get; }

    // A folder per content root under the user's local application data.
    // Outside the project, so a cooked sound never reaches a pack or source
    // control.
    public static string DirectoryFor(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot));

        // Windows opens one folder under any casing, and two spellings must
        // not get two caches.
        string identity = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        ulong id = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(identity));

        // The project folder's name in front, so a person can tell which is which.
        string project = Path.GetFileName(Path.GetDirectoryName(root)) ?? string.Empty;
        string folder = project.Length > 0 ? $"{project}-{id:X16}" : $"{id:X16}";

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local.Length > 0 ? local : Path.GetTempPath(), "Spectra", "SoundCache", folder);
    }

    // Null when the sound has no entry, or has one that cannot be read.
    public SoundCacheEntry? TryOpen(string cookedPath)
    {
        FileStream stream;
        try
        {
            // Delete is shared so another editor can replace the entry meanwhile.
            stream = new FileStream(
                EntryPath(cookedPath), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            return SoundCacheEntry.Read(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
        {
            stream.Dispose();
            return null;
        }
    }

    public void Write(string cookedPath, LooseSoundResult result)
    {
        string path = EntryPath(cookedPath);
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
            System.IO.Directory.CreateDirectory(folder);

        // Never a half-written entry under the real name.
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = File.Create(temporary))
                SoundCacheEntry.Write(stream, result);

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private string EntryPath(string cookedPath) =>
        ContentRoot.ResolveAbsolute(Directory, cookedPath) + EntryExtension;
}
