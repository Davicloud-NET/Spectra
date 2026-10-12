using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>
/// The script API of this build as data: every class, value type, library
/// and global a script can reach. Reading it loads no native library.
/// </summary>
public static class LuauScriptApi
{
    /// <summary>
    /// What the binding was generated from. <see cref="LuauDefinitions"/>
    /// writes it for luau-lsp, and <see cref="ScriptApiJson"/> for the docs.
    /// </summary>
    public static ScriptApiDescription Description => ScriptApiCatalog.Description;
}
