using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>A global table of functions and constants, such as <c>task</c>.</summary>
public sealed record ScriptLibraryDescription
{
    /// <summary>The name of the global.</summary>
    public required string Name { get; init; }

    /// <summary>What it is for.</summary>
    public string Summary { get; init; } = "";

    /// <summary>Its functions.</summary>
    public IReadOnlyList<ScriptFunctionDescription> Functions { get; init; } = [];

    /// <summary>Its constants.</summary>
    public IReadOnlyList<ScriptPropertyDescription> Constants { get; init; } = [];
}
