using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>A class a script holds objects of, or a type it holds by value.</summary>
public sealed record ScriptClassDescription
{
    /// <summary>What a script calls it.</summary>
    public required string Name { get; init; }

    /// <summary>The class it has every member of, or empty.</summary>
    public string Extends { get; init; } = "";

    /// <summary>Whether a script holds it by value, as it does a <c>CFrame</c>.</summary>
    public bool IsValue { get; init; }

    /// <summary>Its type parameters as Luau writes them, or empty.</summary>
    public string TypeParameters { get; init; } = "";

    /// <summary>The class a child named like a member reads as to a type checker, or empty.</summary>
    public string ChildType { get; init; } = "";

    /// <summary>What it is for.</summary>
    public string Summary { get; init; } = "";

    /// <summary>Its properties.</summary>
    public IReadOnlyList<ScriptPropertyDescription> Properties { get; init; } = [];

    /// <summary>Its methods.</summary>
    public IReadOnlyList<ScriptFunctionDescription> Methods { get; init; } = [];

    /// <summary>Its signals.</summary>
    public IReadOnlyList<ScriptSignalDescription> Signals { get; init; } = [];

    /// <summary>The operators it can stand on the left of.</summary>
    public IReadOnlyList<ScriptOperatorDescription> Operators { get; init; } = [];
}
