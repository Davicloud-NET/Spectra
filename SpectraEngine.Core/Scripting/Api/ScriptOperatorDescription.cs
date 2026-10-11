namespace SpectraEngine.Core.Scripting.Api;

/// <summary>An operator a value type can stand on the left of.</summary>
/// <param name="Operator">The operator as a script writes it, or <c>unm</c> for the minus in front.</param>
/// <param name="Right">The type on its right, as Luau writes it. Empty for <c>unm</c>.</param>
/// <param name="Returns">The type of the result.</param>
public sealed record ScriptOperatorDescription(string Operator, string Right, string Returns);
