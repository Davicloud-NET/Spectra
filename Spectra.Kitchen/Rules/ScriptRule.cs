using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Scripts;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Scripting;
using System;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Checks a <c>.luau</c> script and packs its text behind a small header, at
/// the script's own path. A script that does not compile fails the cook, and
/// so does one nothing here can check.
/// </summary>
// The pack carries text and the game compiles it at load. Luau does not keep
// its bytecode readable between releases, so text is what stays loadable.
public sealed class ScriptRule : IRule
{
    private readonly IScriptSyntaxCheck _check;

    /// <summary>Creates the rule over what checks a script.</summary>
    public ScriptRule(IScriptSyntaxCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _check = check;
    }

    /// <inheritdoc/>
    public RuleKind Kind => RuleKind.Script;

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    // The compiler's release is in every key as a tool version.
    public CookSettingKeys SettingsRead => CookSettingKeys.None;

    /// <summary>Whether <paramref name="contentPath"/> is a script this rule cooks.</summary>
    public static bool Handles(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);
        return contentPath.EndsWith(PackedScript.FileExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public void Cook(IRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        byte[] source = context.Read(context.SourcePath);

        // Unchecked is refused, not packed: a pack is what ships, and nothing
        // in one says its scripts were never compiled.
        if (_check.Problem is { } problem)
        {
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.ScriptNotChecked,
                $"'{context.SourcePath}' cannot be checked, so it is not packed. {problem}",
                context.SourcePath));

            return;
        }

        if (_check.Check(source) is { } mistake)
        {
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.ScriptSyntaxError,
                mistake.Message,
                context.SourcePath,
                mistake.Line,
                mistake.Column));

            return;
        }

        context.Emit(context.SourcePath, PackedScriptWriter.WriteSource(source), PackEntryKind.Script);
    }
}
