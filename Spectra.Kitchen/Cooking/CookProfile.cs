namespace Spectra.Kitchen.Cooking;

/// <summary>
/// What a cook is for. Rules may branch on it.
/// </summary>
public enum CookProfile
{
    /// <summary>Everything at final quality. The default, and what a build server runs.</summary>
    Ship,

    /// <summary>Quality traded for cook time, for a local iteration loop.</summary>
    Fast,

    /// <summary>What the editor's cooked-accurate preview asks for.</summary>
    Preview,
}

/// <summary>Which block-compression encoder a cook uses.</summary>
// Managed has to stay the default: the AOT editor hosts this library in process.
public enum CookEncoder
{
    /// <summary>Pure managed, no native dependency.</summary>
    Managed,

    /// <summary>An opt-in native encoder, for cook throughput on a build machine.</summary>
    Native,
}
