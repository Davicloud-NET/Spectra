using Spectra.Kitchen.Cache;
using SpectraEngine.Core.Assets.Packs;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Copies an asset into the pack unchanged. The fallback for files with no cooked
/// format, so a packed build resolves the same content a loose one does.
/// </summary>
public sealed class RawCopyRule : IRule
{
    /// <inheritdoc/>
    public RuleKind Kind => RuleKind.RawCopy;

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    // A copy does not vary by any setting, so one cached copy serves every profile.
    public CookSettingKeys SettingsRead => CookSettingKeys.None;

    /// <inheritdoc/>
    public void Cook(IRuleContext context)
    {
        context.Copy(context.SourcePath, context.SourcePath, PackEntryKind.Raw);
    }
}
