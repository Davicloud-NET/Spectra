using System;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// Says whether a script compiles, by the compiler a game loads it with. A
/// cook asks before it packs a script. Several threads may ask at once.
/// </summary>
public interface IScriptSyntaxCheck
{
    /// <summary>
    /// The compiler and its release, such as <c>luau 0.742</c>. A cook keys
    /// its cache on it: another release may judge the same script another way.
    /// </summary>
    string CompilerVersion { get; }

    /// <summary>
    /// Why no script can be checked here and what to do about it, or null
    /// when one can.
    /// </summary>
    string? Problem { get; }

    /// <summary>The first mistake in a script, or null when it compiles.</summary>
    /// <param name="source">The script's text as UTF-8, as its file holds it.</param>
    ScriptSyntaxError? Check(ReadOnlySpan<byte> source);
}
