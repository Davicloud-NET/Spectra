using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Core.Serialization;
using System;
using System.Text;
using System.Text.Json;

namespace SpectraEngine.Core.Scripting.Tooling;

/// <summary>
/// The files beside a project's Assets folder that set a code editor up for
/// its scripts: where each one is, and what the engine writes into it.
/// </summary>
public static class ScriptProjectFiles
{
    /// <summary>
    /// The script API for luau-lsp. The engine's own file: it is written
    /// again whenever its text is not this engine's.
    /// </summary>
    public const string Definitions = LuauDefinitions.FileName;

    /// <summary>Luau's settings file, which holds the alias of the content root.</summary>
    public const string LuauRc = ".luaurc";

    /// <summary>What Visual Studio Code reads for the folder: where luau-lsp finds the definitions.</summary>
    public const string CodeSettings = ".vscode/settings.json";

    /// <summary>What Visual Studio Code offers to install for the folder.</summary>
    public const string CodeExtensions = ".vscode/extensions.json";

    /// <summary>
    /// The alias a script's require takes for the content root, as in
    /// <c>require("@assets/Shared/util")</c>.
    /// </summary>
    public const string AssetsAlias = "assets";

    /// <summary>The name luau-lsp knows the definitions file by.</summary>
    public const string DefinitionsName = "@spectra";

    /// <summary>The extension that runs luau-lsp in Visual Studio Code.</summary>
    public const string LuauLspExtension = "JohnnyMorganz.luau-lsp";

    /// <summary>
    /// The text of <see cref="LuauRc"/>: the alias and nothing else. The
    /// language mode is the choice of whoever writes the scripts.
    /// </summary>
    public static string LuauRcText() => Json(writer =>
    {
        writer.WriteStartObject("aliases");
        writer.WriteString(AssetsAlias, "./" + ProjectFormat.AssetsFolder);
        writer.WriteEndObject();
    });

    /// <summary>The text of <see cref="CodeSettings"/>.</summary>
    // The extension starts as a Roblox tool: its own types for game and
    // workspace, and a tree of the place read from a Rojo sourcemap.
    public static string CodeSettingsText() => Json(writer =>
    {
        writer.WriteString("luau-lsp.platform.type", "standard");
        writer.WriteStartObject("luau-lsp.types.definitionFiles");
        writer.WriteString(DefinitionsName, Definitions);
        writer.WriteEndObject();
        writer.WriteBoolean("luau-lsp.sourcemap.enabled", false);
    });

    /// <summary>The text of <see cref="CodeExtensions"/>.</summary>
    public static string CodeExtensionsText() => Json(writer =>
    {
        writer.WriteStartArray("recommendations");
        writer.WriteStringValue(LuauLspExtension);
        writer.WriteEndArray();
    });

    private static string Json(Action<Utf8JsonWriter> members) =>
        Encoding.UTF8.GetString(CanonicalJson.Write(writer =>
        {
            writer.WriteStartObject();
            members(writer);
            writer.WriteEndObject();
        }));
}
