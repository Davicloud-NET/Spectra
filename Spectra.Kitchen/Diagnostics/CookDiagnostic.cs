using System;

namespace Spectra.Kitchen.Diagnostics;

/// <summary>
/// One thing the cook has to say, in the form an IDE can jump to.
/// </summary>
public sealed record CookDiagnostic
{
    private CookDiagnostic(
        CookDiagnosticId id,
        CookDiagnosticSeverity severity,
        string message,
        string? file,
        int line,
        int column)
    {
        Id = id;
        Severity = severity;
        Message = message;
        File = file;
        Line = line;
        Column = column;
    }

    /// <summary>The code, <c>SC####</c> or a wrapped foreign one.</summary>
    public CookDiagnosticId Id { get; }

    /// <summary>How loud it is.</summary>
    public CookDiagnosticSeverity Severity { get; init; }

    /// <summary>What went wrong, as a sentence.</summary>
    public string Message { get; }

    /// <summary>The file it is about, or null when it is about the run itself.</summary>
    public string? File { get; }

    /// <summary>One-based line, or zero when the diagnostic is about the whole file.</summary>
    public int Line { get; }

    /// <summary>One-based column, or zero.</summary>
    public int Column { get; }

    /// <summary>Whether this diagnostic fails the cook.</summary>
    public bool IsError => Severity == CookDiagnosticSeverity.Error;

    public static CookDiagnostic Error(CookDiagnosticId id, string message, string? file = null, int line = 0, int column = 0) =>
        new(id, CookDiagnosticSeverity.Error, message, file, line, column);

    public static CookDiagnostic Warning(CookDiagnosticId id, string message, string? file = null, int line = 0, int column = 0) =>
        new(id, CookDiagnosticSeverity.Warning, message, file, line, column);

    public static CookDiagnostic Info(CookDiagnosticId id, string message, string? file = null, int line = 0, int column = 0) =>
        new(id, CookDiagnosticSeverity.Info, message, file, line, column);

    /// <summary>
    /// This diagnostic as an error, for <c>--strict</c>. Returns the same instance
    /// when it is already one.
    /// </summary>
    public CookDiagnostic AsError() =>
        Severity == CookDiagnosticSeverity.Error ? this : this with { Severity = CookDiagnosticSeverity.Error };

    /// <summary>The severity word MSBuild matches on.</summary>
    public static string SeverityText(CookDiagnosticSeverity severity) => severity switch
    {
        CookDiagnosticSeverity.Error => "error",
        CookDiagnosticSeverity.Warning => "warning",
        _ => "info",
    };

    /// <summary>
    /// The IDE-parseable line, without colour: either
    /// <c>&lt;file&gt;(&lt;line&gt;,&lt;col&gt;): error SC0001: message</c> or
    /// <c>&lt;tool&gt; : error SC0001: message</c>.
    /// </summary>
    public string ToBuildLine(string toolName) =>
        $"{Origin(toolName)} {SeverityText(Severity)} {Id}: {Message}";

    /// <summary>
    /// The part before the severity, colon included: <c>scook :</c> or
    /// <c>file(1,1):</c>.
    /// </summary>
    // MSBuild wants a space before the colon in the tool form and none in the
    // file form, so the colon is part of the origin.
    public string Origin(string toolName)
    {
        if (File is null) return $"{toolName} :";

        return Line > 0
            ? $"{File}({Math.Max(1, Line)},{Math.Max(1, Column)}):"
            : $"{File}:";
    }

    /// <inheritdoc/>
    public override string ToString() => ToBuildLine("scook");
}
