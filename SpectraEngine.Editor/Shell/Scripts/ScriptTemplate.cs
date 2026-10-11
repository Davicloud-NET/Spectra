using System;
using System.IO;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>
/// What a new script's file holds, read from the file in the editor's assets
/// that Insert Script copies.
/// </summary>
public static class ScriptTemplate
{
    // Assets/Templates/Script.luau, as the build embeds it.
    private const string ResourceName = "SpectraEngine.Editor.Assets.Templates.Script.luau";

    private static readonly Lazy<string> Text = new(Load);

    /// <summary>The template's text, with a line feed at the end of each line.</summary>
    public static string Read() => Text.Value;

    private static string Load()
    {
        using Stream stream = typeof(ScriptTemplate).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The editor was built without {ResourceName}.");
        using var reader = new StreamReader(stream);

        // A checkout on Windows may hand the build carriage returns. A new
        // script should be the same bytes whoever built the editor.
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
