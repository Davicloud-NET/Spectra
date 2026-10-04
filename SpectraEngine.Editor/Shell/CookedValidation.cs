using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Packs;
using SpectraEngine.Core.Projects;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Editor.Shell;

internal sealed record CookedValidationReport(
    bool Succeeded, string Summary, IReadOnlyList<CookDiagnostic> Diagnostics);

// Cooks the open project and verifies the pack with only itself mounted.
// Always cooks first, so a stale pack in cooked/ cannot pass. Cooks what is on
// disk; unsaved edits are not included.
internal static class CookedValidation
{
    // Touches no scene state. Any thread but the render one.
    public static CookedValidationReport Run(ProjectLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var diagnostics = new List<CookDiagnostic>();

        CookResult cooked;
        try
        {
            cooked = new CookSession(layout, new CookSettings()).Run();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CookedValidationReport(false, $"The cook could not run: {ex.Message}", diagnostics);
        }

        diagnostics.AddRange(cooked.Diagnostics);

        if (!cooked.Succeeded || cooked.OutputPath is not { } pack)
        {
            return new CookedValidationReport(
                false,
                $"The cook failed with {cooked.ErrorCount} error(s), so there is no pack to validate.",
                diagnostics);
        }

        PackVerifyResult verified;
        try
        {
            verified = PackVerifier.Verify(pack);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CookedValidationReport(false, $"Could not read '{pack}': {ex.Message}", diagnostics);
        }

        diagnostics.AddRange(verified.Diagnostics);

        string name = Path.GetFileName(pack);
        string summary = verified.Succeeded
            ? $"Cooked content validates: {name}, {verified.EntriesChecked} entries, " +
              $"{verified.ReferencesChecked} reference(s) resolved with only the pack mounted."
            : $"Cooked content is broken: {verified.ErrorCount} error(s) in {name}. " +
              "The running editor hides these, because it resolves the loose files instead.";

        return new CookedValidationReport(verified.Succeeded, summary, diagnostics);
    }
}
