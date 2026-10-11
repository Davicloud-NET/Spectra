namespace SpectraEngine.Core.Scripting;

/// <summary>One function on a script's call stack.</summary>
/// <param name="Script">The script file, as a content path.</param>
/// <param name="Line">The line it was on, or 0 when that is not known.</param>
/// <param name="Function">The function's name, or null for a script's top level and for a function with no name.</param>
public readonly record struct ScriptFrame(string Script, int Line, string? Function);
