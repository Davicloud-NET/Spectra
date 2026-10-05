using Spectra.Kitchen.Diagnostics;
using System.Collections.Generic;

namespace Spectra.Kitchen.Cooking;

/// <summary>What <see cref="LooseSoundFolder"/> did with one folder of sounds.</summary>
/// <param name="Cooked">Sounds that were cooked and written.</param>
/// <param name="UpToDate">Sounds left alone because their cooked file was newer.</param>
/// <param name="Diagnostics">What the cook said, in the order the sounds were walked.</param>
public sealed record LooseSoundFolderResult(
    int Cooked, int UpToDate, IReadOnlyList<CookDiagnostic> Diagnostics)
{
    /// <summary>Diagnostics that are errors.</summary>
    public int ErrorCount => Count(CookDiagnosticSeverity.Error);

    /// <summary>Diagnostics that are warnings.</summary>
    public int WarningCount => Count(CookDiagnosticSeverity.Warning);

    /// <summary>Whether every sound that needed cooking was cooked and written.</summary>
    public bool Succeeded => ErrorCount == 0;

    private int Count(CookDiagnosticSeverity severity)
    {
        int count = 0;
        foreach (CookDiagnostic diagnostic in Diagnostics)
        {
            if (diagnostic.Severity == severity) count++;
        }

        return count;
    }
}
