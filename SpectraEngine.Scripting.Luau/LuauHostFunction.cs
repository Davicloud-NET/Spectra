namespace SpectraEngine.Scripting.Luau;

/// <summary>
/// A function a script can call. Its arguments are the stack's values from 1
/// up. It answers with how many of the top values are its results, or with
/// <see cref="LuauStack.Fail(ReadOnlySpan{char})"/> or <see cref="LuauStack.Yield"/>.
/// </summary>
public delegate int LuauHostFunction(LuauStack stack);
