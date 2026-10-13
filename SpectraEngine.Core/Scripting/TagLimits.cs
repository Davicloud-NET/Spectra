namespace SpectraEngine.Core.Scripting;

/// <summary>
/// How long a tag is and how many one node has. Tags are kept outside the
/// scripts' own memory, so these are what bounds them.
/// </summary>
public static class TagLimits
{
    /// <summary>The longest tag, in bytes of UTF-8.</summary>
    public const int MaxBytes = 256;

    /// <summary>The most tags one node has.</summary>
    public const int MaxPerNode = 256;
}
