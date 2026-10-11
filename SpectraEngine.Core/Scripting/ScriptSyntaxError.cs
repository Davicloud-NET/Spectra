namespace SpectraEngine.Core.Scripting;

/// <summary>A mistake that keeps a script from compiling.</summary>
/// <param name="Line">The line, counted from one, or zero when the compiler names none.</param>
/// <param name="Column">The column, counted from one, or zero when the compiler names none.</param>
/// <param name="Message">What is wrong, in the compiler's words.</param>
public readonly record struct ScriptSyntaxError(int Line, int Column, string Message);
