namespace SpectraEngine.Core.Scripting;

/// <summary>
/// How much of attributes one node holds. What scripts set is kept outside
/// their own memory, so these are what bounds it.
/// </summary>
public static class AttributeLimits
{
    /// <summary>The longest text an attribute holds, in bytes of UTF-8.</summary>
    public const int MaxTextBytes = 65536;

    /// <summary>The most attributes one node holds, and the most names it keeps a signal for.</summary>
    public const int MaxPerNode = 256;
}
