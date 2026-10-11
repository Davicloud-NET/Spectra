using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Declares a signal a script connects a function to. It goes on the
/// property that gives the signal.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, Inherited = false)]
public sealed class ScriptSignalAttribute : Attribute
{
    /// <summary>Declares a signal by what it hands its listeners.</summary>
    /// <param name="parameters">As Luau writes them: <c>time: number, deltaTime: number</c>. Empty for nothing.</param>
    public ScriptSignalAttribute(string parameters = "") => Parameters = parameters;

    /// <summary>What a listener is handed, as Luau writes it.</summary>
    public string Parameters { get; }

    /// <summary>What a script calls it, when that is not the member's own name.</summary>
    public string? Name { get; set; }
}
