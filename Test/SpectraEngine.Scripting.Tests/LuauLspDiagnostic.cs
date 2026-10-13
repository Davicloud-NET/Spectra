namespace SpectraEngine.Scripting.Tests;

// One thing luau-lsp said of a script: a type error or a lint.
internal sealed record LuauLspDiagnostic(string File, int Line, int Column, string Kind, string Text)
{
    public override string ToString() => $"{File}({Line},{Column}): {Kind}: {Text}";
}
