using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Declares a method of a class or a value. Its parameters and what it
/// returns are the member's own. A method written by hand takes the stack
/// instead and says both here.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ScriptMethodAttribute : Attribute
{
    /// <summary>What a script calls it, when that is not the member's own name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The parameters of a method written by hand, as Luau writes them:
    /// <c>goal: CFrame, alpha: number?</c>. Without the object itself.
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>What a method written by hand returns, as Luau writes it. Null for nothing.</summary>
    public string? Returns { get; set; }

    /// <summary>The binding is written and installed by hand, and this only describes it.</summary>
    public bool ByHand { get; set; }
}
