using System;

namespace Spectra.Kitchen.Diagnostics;

/// <summary>
/// The code half of a cook diagnostic: <c>SC####</c>, or a foreign code carried
/// through unchanged. Retired codes are never reused.
/// </summary>
public readonly record struct CookDiagnosticId
{
    /// <summary>The prefix a code the cooker owns is spelled with.</summary>
    public const string CookPrefix = "SC";

    private readonly string? _prefix;

    private CookDiagnosticId(string? prefix, int number)
    {
        _prefix = prefix;
        Number = number;
    }

    /// <summary>The two-letter prefix. <c>SC</c> for a code the cooker owns.</summary>
    public string Prefix => _prefix ?? CookPrefix;

    /// <summary>The four-digit number, whose thousands digit is the band.</summary>
    public int Number { get; }

    /// <summary>Whether this code belongs to the cooker rather than to a wrapped tool.</summary>
    public bool IsCookCode => _prefix is null || _prefix == CookPrefix;

    /// <summary>The band: 0 for project and CLI, 9 for pack writing, and so on.</summary>
    public int Band => Number / 1000;

    /// <summary>Allocates a code the cooker owns. Throws for a retired code.</summary>
    public static CookDiagnosticId Cook(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 9999);

        if (CookDiagnosticCodes.IsRetired(number))
        {
            throw new ArgumentOutOfRangeException(
                nameof(number), number,
                $"{CookPrefix}{number:D4} is retired and may never be reused: every bug report and " +
                "suppression naming it would then mean something else.");
        }

        return new CookDiagnosticId(CookPrefix, number);
    }

    /// <summary>
    /// Carries a foreign tool's code through unchanged, so the cooker reports a
    /// shader error under the code <c>ssc</c> and the language server report.
    /// </summary>
    /// <param name="prefix">The owning tool's prefix, <c>SS</c> for SpectraShade.</param>
    public static CookDiagnosticId Wrap(string prefix, int number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentOutOfRangeException.ThrowIfNegative(number);

        return new CookDiagnosticId(prefix, number);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Prefix}{Number:D4}";
}
