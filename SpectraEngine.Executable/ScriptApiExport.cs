using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;
using System.IO;
using System.Text;

namespace SpectraEngine.Executable;

// Writes what a script can reach in this build, for the tools that read it:
// the definitions file for luau-lsp, and JSON for the docs site. Both come
// from the description the binding was generated from. No Luau state is
// opened, so this works in a build with no native library.
internal static class ScriptApiExport
{
    // Writes each file that has a path. The files are build output.
    public static void Write(string? typesPath, string? jsonPath, ILogger logger)
    {
        ScriptApiDescription api = LuauScriptApi.Description;

        // No byte order mark: luau-lsp reads the file as it is.
        if (typesPath is not null)
            Save(typesPath, new UTF8Encoding(false).GetBytes(LuauDefinitions.Write(api)), api, logger);
        if (jsonPath is not null)
            Save(jsonPath, ScriptApiJson.Write(api), api, logger);
    }

    private static void Save(string path, byte[] bytes, ScriptApiDescription api, ILogger logger)
    {
        string destination = Path.GetFullPath(path);
        if (Path.GetDirectoryName(destination) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(destination, bytes);
        logger.LogInformation(
            "Exported the script API to {Path} ({Bytes} bytes: {Classes} classes, {Libraries} libraries, {Globals} globals)",
            destination, bytes.Length, api.Classes.Count, api.Libraries.Count, api.Globals.Count + api.Functions.Count);
    }
}
