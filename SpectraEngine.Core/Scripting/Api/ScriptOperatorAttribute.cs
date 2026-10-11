using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Declares an operator of a value type: what stands on its right and what
/// comes out. The binding of an operator is written by hand, or is Luau's own.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ScriptOperatorAttribute : Attribute
{
    /// <summary>Declares one operator with one type on its right.</summary>
    /// <param name="operator">
    /// The operator as a script writes it: <c>+</c>, <c>-</c>, <c>*</c>,
    /// <c>/</c>, <c>//</c>, <c>==</c>, or <c>unm</c> for the minus in front.
    /// </param>
    /// <param name="right">The type on its right, as Luau writes it. Empty for <c>unm</c>.</param>
    /// <param name="returns">The type of the result, as Luau writes it.</param>
    public ScriptOperatorAttribute(string @operator, string right, string returns)
    {
        Operator = @operator;
        Right = right;
        Returns = returns;
    }

    /// <summary>The operator as a script writes it.</summary>
    public string Operator { get; }

    /// <summary>The type on its right.</summary>
    public string Right { get; }

    /// <summary>The type of the result.</summary>
    public string Returns { get; }
}
