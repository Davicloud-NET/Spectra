using Spectra.Kitchen.Diagnostics;

namespace Spectra.Kitchen.CLI;

// Writes cook diagnostics in the form MSBuild and IDEs parse. Only adds colour:
// the text is CookDiagnostic.ToBuildLine's, which the editor also uses.
// All severities go to stderr so stdout stays the tool's result.
internal sealed class DiagnosticWriter(TextWriter output, string toolName, bool color)
{
    public void WriteAll(IReadOnlyList<CookDiagnostic> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++)
            Write(diagnostics[i]);
    }

    public void Write(CookDiagnostic diagnostic)
    {
        string origin = diagnostic.Origin(toolName);
        string severity = CookDiagnostic.SeverityText(diagnostic.Severity);

        if (!color)
        {
            output.WriteLine(diagnostic.ToBuildLine(toolName));
            return;
        }

        string tint = SeverityColor(diagnostic.Severity);
        output.WriteLine($"{origin} \u001b[{tint};1m{severity} {diagnostic.Id}\u001b[0m: {diagnostic.Message}");
    }

    private static string SeverityColor(CookDiagnosticSeverity severity) => severity switch
    {
        CookDiagnosticSeverity.Error => "31",
        CookDiagnosticSeverity.Warning => "33",
        _ => "36",
    };
}
