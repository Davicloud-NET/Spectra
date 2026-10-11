using SpectraEngine.Core.Assets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpectraEngine.Editing.Commands;

/// <summary>Writes the file a new script starts as, and never over one that is there.</summary>
public static class ScriptFileMaker
{
    // No byte order mark: a script is read by tools that take one for text.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Makes <c>Scripts/Name.luau</c> under <paramref name="contentRoot"/>, named
    /// after the node the script goes under, with a number when that is taken.
    /// </summary>
    /// <param name="contentPath">The new file, as the path a level stores.</param>
    /// <param name="failure">Why no file was made, when the answer is false.</param>
    public static bool TryMake(
        string contentRoot, string? parentName, string text, out string contentPath, out string failure)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TryMake(
            contentRoot, parentName, stream => stream.Write(Utf8.GetBytes(text)), out contentPath, out failure);
    }

    /// <summary>
    /// Makes the file as <see cref="TryMake(string, string?, string, out string, out string)"/>
    /// does, and hands it to <paramref name="fill"/> to write.
    /// </summary>
    public static bool TryMake(
        string contentRoot, string? parentName, Action<Stream> fill, out string contentPath, out string failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(fill);

        contentPath = string.Empty;
        failure = string.Empty;

        string folder = Path.Combine(contentRoot, ScriptFileName.Folder);
        string stem = ScriptFileName.StemFor(parentName);
        string shown = Shown(stem + ScriptFileName.Extension);

        try
        {
            Directory.CreateDirectory(folder);
            HashSet<string> taken = NamesIn(folder);

            // More than one try: a file can appear between the look and the write.
            while (ScriptFileName.FirstFree(stem, taken.Contains) is { } name)
            {
                shown = Shown(name);
                if (TryWriteNew(Path.Combine(folder, name), fill))
                {
                    contentPath = ScriptFileName.Folder + "/" + name;
                    return true;
                }

                taken.Add(name);
            }

            failure = $"{shown} and every numbered name after it are taken.";
            return false;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A folder that cannot be written, a path too long, a full disk.
            failure = $"{shown} could not be written. {ex.Message}";
            return false;
        }
    }

    // Without regard to case, as Windows and a cooked pack compare names.
    private static HashSet<string> NamesIn(string folder)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFileSystemEntries(folder))
            names.Add(Path.GetFileName(path));

        return names;
    }

    // False when the file is there after all. CreateNew is what keeps this
    // from ever writing over somebody's script.
    private static bool TryWriteNew(string fullPath, Action<Stream> fill)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(fullPath))
        {
            return false;
        }

        bool isFilled = false;
        try
        {
            using (stream)
                fill(stream);

            isFilled = true;
            return true;
        }
        finally
        {
            // Made and not filled: a full disk, a share that dropped. Left
            // there it would take the name, empty.
            if (!isFilled)
                RemoveUnfilled(fullPath);
        }
    }

    private static void RemoveUnfilled(string fullPath)
    {
        try
        {
            File.Delete(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The write's own failure is the one to report.
        }
    }

    private static string Shown(string name) =>
        ContentRoot.DirectoryName + "/" + ScriptFileName.Folder + "/" + name;
}
