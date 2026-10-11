namespace SpectraEngine.Core.Scripting.Api;

/// <summary>A property of a class or a value, a constant of a library, or a global value.</summary>
public sealed record ScriptPropertyDescription
{
    /// <summary>What a script calls it.</summary>
    public required string Name { get; init; }

    /// <summary>Its type, as Luau writes it.</summary>
    public required string Type { get; init; }

    /// <summary>What it is.</summary>
    public string Summary { get; init; } = "";

    /// <summary>
    /// The member of <see cref="ScriptWrites"/> a write goes through. Null
    /// for a property a script cannot set.
    /// </summary>
    public SettableMemberId? Sets { get; init; }

    /// <summary>What to write in its place when it is an old spelling, or empty.</summary>
    public string Deprecated { get; init; } = "";
}
