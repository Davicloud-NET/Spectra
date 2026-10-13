using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Entities;
using SpectraEngine.Scripting.Luau.Runtime;
using System.IO;
using System.Text;

namespace SpectraEngine.Executable;

// Writes what a script can reach in this build: the definitions file for
// luau-lsp, and JSON for the docs site. Both come from the description the
// binding was generated from, with the built-in entity classes read from
// their schemas. No Luau state is opened, so no native library is needed.
internal static class ScriptApiExport
{
    // Writes each file that has a path. The files are build output.
    public static void Write(string? typesPath, string? jsonPath, ILogger logger)
    {
        IReadOnlyList<EntitySchema> schemas = BuiltinSchemas();
        ScriptApiDescription api = LuauScriptApi.Description.WithEntityClasses(schemas);

        // A class or a member the files leave out is left out without a word.
        foreach (string gap in EntityClassGaps.Of(schemas, LuauScriptApi.Description))
            logger.LogWarning("{Gap}", gap);

        // No byte order mark: luau-lsp reads the file as it is.
        if (typesPath is not null)
            Save(typesPath, new UTF8Encoding(false).GetBytes(LuauDefinitions.Write(api)), api, logger);
        if (jsonPath is not null)
            Save(jsonPath, ScriptApiJson.Write(api), api, logger);
    }

    // Through .sentdef bytes, as an editor learns them, so the file says of a
    // class what a project's own schema file would. The catalogue the
    // classes register in is not read.
    private static IReadOnlyList<EntitySchema> BuiltinSchemas() =>
        EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(BuiltinEntities.Schemas)).Schemas;

    private static void Save(string path, byte[] bytes, ScriptApiDescription api, ILogger logger)
    {
        string destination = Path.GetFullPath(path);
        if (Path.GetDirectoryName(destination) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(destination, bytes);
        logger.LogInformation(
            "Exported the script API to {Path} ({Bytes} bytes: {Classes} classes, {Entities} entity classes, " +
            "{Libraries} libraries, {Globals} globals)",
            destination, bytes.Length, api.Classes.Count, api.EntityClasses.Count, api.Libraries.Count,
            api.Globals.Count + api.Functions.Count);
    }
}
