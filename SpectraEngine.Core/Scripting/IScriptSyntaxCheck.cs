using System;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// Says whether a script compiles, by the compiler a game loads it with. A
/// cook asks before it packs a script. Several threads may ask at once.
/// </summary>
public interface IScriptSyntaxCheck
{
    /// <summary>
    /// The compiler, its release and how it is set up, such as
    /// <c>luau 0.742, shim 7, O1 g1</c>. Whatever can change a verdict is in
    /// it, because a cook keys a script's cache entry on it.
    /// </summary>
    string CompilerVersion { get; }

    /// <summary>
    /// Why no script can be checked here and what to do about it, or null
    /// when one can.
    /// </summary>
    string? Problem { get; }

    /// <summary>The first mistake in a script, or null when it compiles.</summary>
    /// <param name="source">The script's text as UTF-8, as its file holds it.</param>
    /// <exception cref="InvalidOperationException"><see cref="Problem"/> is set: nothing here can judge a script.</exception>
    ScriptSyntaxError? Check(ReadOnlySpan<byte> source);
}
