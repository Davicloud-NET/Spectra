namespace SpectraEngine.Scripting.Luau;

/// <summary>What a stack slot was when a managed object was asked for.</summary>
public enum LuauObjectLookup
{
    /// <summary>The object.</summary>
    Found,

    /// <summary>Something else than an object of the kind asked for.</summary>
    NotThisKind,

    /// <summary>A handle to an object the host has let go of.</summary>
    Released,
}
