using SpectraEngine.Core.Scripting;
using System;

namespace Spectra.Kitchen.Scripts;

/// <summary>
/// The check of a cook whose host installed none. It checks nothing and says
/// so, and a project with scripts then fails to cook.
/// </summary>
public sealed class NoScriptSyntaxCheck : IScriptSyntaxCheck
{
    private NoScriptSyntaxCheck()
    {
    }

    /// <summary>The one instance.</summary>
    public static NoScriptSyntaxCheck Instance { get; } = new();

    /// <inheritdoc/>
    public string CompilerVersion => "none";

    /// <inheritdoc/>
    public string Problem =>
        "The program that runs this cook gave it no Luau compiler to check a script with. " +
        "Cook with scook, which has one.";

    /// <inheritdoc/>
    // Never asked: the rule reads Problem first.
    public ScriptSyntaxError? Check(ReadOnlySpan<byte> source) => null;
}
