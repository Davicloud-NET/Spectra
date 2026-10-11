using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>One shape a function can be called in.</summary>
/// <param name="Parameters">What it takes. A method's object is not among them.</param>
/// <param name="Returns">What it returns, as Luau writes it: a type, several in brackets, or empty for nothing.</param>
public sealed record ScriptSignature(IReadOnlyList<ScriptParameter> Parameters, string Returns);
