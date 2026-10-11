using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>A method of a class or a value, a function of a library, or a global function.</summary>
public sealed record ScriptFunctionDescription
{
    /// <summary>What a script calls it.</summary>
    public required string Name { get; init; }

    /// <summary>What it does.</summary>
    public string Summary { get; init; } = "";

    /// <summary>The shapes it can be called in. One for most.</summary>
    public IReadOnlyList<ScriptSignature> Signatures { get; init; } = [];

    /// <summary>What to write in its place when it is an old spelling, or empty.</summary>
    public string Deprecated { get; init; } = "";
}
