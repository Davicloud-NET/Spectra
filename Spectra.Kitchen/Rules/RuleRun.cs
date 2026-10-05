using Spectra.Kitchen.Diagnostics;
using System;
using System.IO;

namespace Spectra.Kitchen.Rules;

// Runs a rule and turns what it throws into the diagnostic a cook reports. A
// project cook and a cook of one loose file both run their rules here, so a
// failure reads the same from either.
internal static class RuleRun
{
    // Null when the rule ran to its end. What it reported is on the context.
    public static CookDiagnostic? Cook(IRule rule, IRuleContext context, string fullPath)
    {
        try
        {
            rule.Cook(context);
            return null;
        }
        catch (RuleInputMissingException ex)
        {
            return CookDiagnostic.Error(CookDiagnosticCodes.InputMissing, ex.Message, fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return CookDiagnostic.Error(
                CookDiagnosticCodes.RuleFailed,
                $"The {rule.Kind} rule failed on '{context.SourcePath}': {ex.Message}",
                fullPath);
        }
    }
}
