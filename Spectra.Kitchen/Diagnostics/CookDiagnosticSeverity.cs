namespace Spectra.Kitchen.Diagnostics;

/// <summary>
/// How loud a cook diagnostic is. Ascending, and the same three levels as the
/// shader compiler's <c>DiagnosticSeverity</c>.
/// </summary>
public enum CookDiagnosticSeverity
{
    /// <summary>Something worth saying that changes nothing about the outcome.</summary>
    Info,

    /// <summary>
    /// The cook completed and something in it deserves attention. Under
    /// <c>--strict</c> the session promotes these to errors.
    /// </summary>
    Warning,

    /// <summary>The cook failed. The tool exits 1 and writes no successful pack.</summary>
    Error,
}
