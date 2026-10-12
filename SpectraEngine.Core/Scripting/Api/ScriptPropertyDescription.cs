namespace SpectraEngine.Core.Scripting.Api;

/// <summary>A property of a class or a value, a constant of a library, or a global value.</summary>
public sealed record ScriptPropertyDescription
{
    private readonly bool _canSet;

    /// <summary>What a script calls it.</summary>
    public required string Name { get; init; }

    /// <summary>Its type, as Luau writes it.</summary>
    public required string Type { get; init; }

    /// <summary>What it is.</summary>
    public string Summary { get; init; } = "";

    /// <summary>
    /// The member of <see cref="ScriptWrites"/> a write goes through. Null
    /// for a property that is not of a node, and for one a script cannot set.
    /// </summary>
    public SettableMemberId? Sets { get; init; }

    /// <summary>
    /// Whether a script can set it: a member of a node through
    /// <see cref="Sets"/>, a member of another object through its own class.
    /// </summary>
    public bool CanSet
    {
        get => _canSet || Sets is not null;
        init => _canSet = value;
    }

    /// <summary>What to write in its place when it is an old spelling, or empty.</summary>
    public string Deprecated { get; init; } = "";
}
