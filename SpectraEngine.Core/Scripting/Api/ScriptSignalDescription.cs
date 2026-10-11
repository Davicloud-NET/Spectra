using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>A signal of a class: something a script connects a function to.</summary>
public sealed record ScriptSignalDescription
{
    /// <summary>What a script calls it.</summary>
    public required string Name { get; init; }

    /// <summary>When it fires.</summary>
    public string Summary { get; init; } = "";

    /// <summary>What it hands a listener.</summary>
    public IReadOnlyList<ScriptParameter> Parameters { get; init; } = [];
}
