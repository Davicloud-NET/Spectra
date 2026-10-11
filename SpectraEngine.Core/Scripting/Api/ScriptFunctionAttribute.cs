using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Declares a function of a library, or a global function. Its parameters
/// and what it returns are the member's own. A function written by hand
/// takes the stack instead and says both here.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ScriptFunctionAttribute : Attribute
{
    /// <summary>What a script calls it, when that is not the member's own name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The parameters of a function written by hand, as Luau writes them:
    /// <c>seconds: number?</c>.
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>What a function written by hand returns, as Luau writes it. Null for nothing.</summary>
    public string? Returns { get; set; }

    /// <summary>The binding is written and installed by hand, and this only describes it.</summary>
    public bool ByHand { get; set; }
}
