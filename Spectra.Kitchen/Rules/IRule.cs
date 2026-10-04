using Spectra.Kitchen.Cache;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// One cook rule: turns one authored asset into its cooked outputs. Rules run in
/// parallel, so an implementation must keep no state between <see cref="Cook"/> calls.
/// </summary>
public interface IRule
{
    /// <summary>What this rule cooks. Part of the cache key.</summary>
    RuleKind Kind { get; }

    /// <summary>
    /// Part of the cache key. Raise it in the same commit that changes the rule's
    /// output, or old cached artifacts keep being served.
    /// </summary>
    int Version { get; }

    /// <summary>
    /// The cook settings this rule's output depends on. Part of the cache key.
    /// One too few gives a stale artifact, one too many a needless rebuild.
    /// </summary>
    CookSettingKeys SettingsRead { get; }

    /// <summary>Cooks <see cref="IRuleContext.SourcePath"/>, reading and emitting through the context.</summary>
    void Cook(IRuleContext context);
}
