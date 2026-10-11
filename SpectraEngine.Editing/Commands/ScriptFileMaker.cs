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
    /// Makes <c>Scripts/Name.luau</c> under <paramref name="contentRoot"/> for a
    /// script that goes under a node called <paramref name="parentName"/>, with
    /// a number added when the name is taken. False when the file could not be
    /// written, and <paramref name="failure"/> then says why.
    /// </summary>
    /// <param name="contentPath">The new file, as the path a level stores.</param>
    public static bool TryMake(
        string contentRoot, string? parentName, string text, out string contentPath, out string failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(text);

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
                if (TryWriteNew(Path.Combine(folder, name), text))
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
    private static bool TryWriteNew(string fullPath, string text)
    {
        try
        {
            using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(Utf8.GetBytes(text));
            return true;
        }
        catch (IOException) when (File.Exists(fullPath))
        {
            return false;
        }
    }

    private static string Shown(string name) =>
        ContentRoot.DirectoryName + "/" + ScriptFileName.Folder + "/" + name;
}
