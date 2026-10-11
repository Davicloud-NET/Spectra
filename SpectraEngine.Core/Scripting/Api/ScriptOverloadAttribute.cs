using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// One of several shapes a function written by hand can be called in, as
/// <c>CFrame.new</c> can. With any of these, they are all of its shapes.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ScriptOverloadAttribute : Attribute
{
    /// <summary>Declares one shape.</summary>
    /// <param name="parameters">As Luau writes them. Empty for none.</param>
    /// <param name="returns">What this shape returns, as Luau writes it. Empty for nothing.</param>
    public ScriptOverloadAttribute(string parameters, string returns = "")
    {
        Parameters = parameters;
        Returns = returns;
    }

    /// <summary>The parameters of this shape.</summary>
    public string Parameters { get; }

    /// <summary>What this shape returns.</summary>
    public string Returns { get; }
}
