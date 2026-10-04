namespace Spectra.Kitchen.Rules;

/// <summary>
/// What a rule cooks. One value per rule, hashed into the cache key, so the
/// numbers are append-only: never renumber or reuse one.
/// </summary>
public enum RuleKind
{
    /// <summary>Bytes copied through unchanged.</summary>
    RawCopy = 1,

    Image = 2,

    Model = 3,

    Audio = 4,

    Material = 5,

    Shader = 6,

    Script = 7,

    Map = 8,
}
