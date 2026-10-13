using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;
using System;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>
/// The definitions file of this build's script API, as the demo writes it
/// for <c>--export-script-types</c>. A project gets it as spectra.d.luau.
/// </summary>
public static class ScriptDefinitions
{
    private static readonly Lazy<string> Text = new(Write);

    /// <summary>The file's text, with a line feed at the end of each line.</summary>
    public static string Read() => Text.Value;

    // The demo's call, word for word, and a test holds the two together.
    // What a build adds to its script API then reaches a project too.
    private static string Write()
    {
        ScriptApiDescription api = LuauScriptApi.Description;
        return LuauDefinitions.Write(api);
    }
}
