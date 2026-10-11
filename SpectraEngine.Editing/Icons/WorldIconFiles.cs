using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SpectraEngine.Editing.Icons;

/// <summary>
/// The icon files of things that stand in the world, as this assembly embeds
/// them. A file's key is its name without the extension.
/// </summary>
// The editor's shell builds its own resources from the same files on disk.
public static class WorldIconFiles
{
    private const string Prefix = "SpectraEngine.Editing.Assets.Icons.";
    private const string Extension = ".svg";

    private static readonly Assembly Home = typeof(WorldIconFiles).Assembly;
    private static readonly string[] Sorted = ReadKeys();
    private static readonly HashSet<string> Known = new(Sorted, StringComparer.Ordinal);

    /// <summary>Every icon's key, sorted.</summary>
    public static IReadOnlyList<string> Keys => Sorted;

    /// <summary>Whether an icon file has this key.</summary>
    public static bool Has(string key) => Known.Contains(key);

    /// <summary>The text of an icon file.</summary>
    /// <exception cref="ArgumentException">No file has this key.</exception>
    public static string Read(string key)
    {
        using Stream stream = Home.GetManifestResourceStream(Prefix + key + Extension)
            ?? throw new ArgumentException($"No world icon is named '{key}'.", nameof(key));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string[] ReadKeys()
    {
        var keys = new List<string>();
        foreach (string name in Home.GetManifestResourceNames())
        {
            if (name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(Extension, StringComparison.Ordinal))
                keys.Add(name[Prefix.Length..^Extension.Length]);
        }

        keys.Sort(StringComparer.Ordinal);
        return [.. keys];
    }
}
