using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Editing.Commands;

/// <summary>One change to an attribute a panel is asking to write to the selection.</summary>
public readonly record struct AttributeEdit
{
    /// <summary>What to do.</summary>
    public required AttributeEditKind Kind { get; init; }

    /// <summary>The attribute's name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The value to set. For <see cref="AttributeEditKind.SetPart"/> only its
    /// kind counts: a node whose attribute is of another kind, or lacks it,
    /// starts from this value.
    /// </summary>
    public MemberValue Value { get; init; }

    /// <summary>Which number <see cref="Number"/> replaces. See <c>AttributeParts</c>.</summary>
    public int Part { get; init; }

    /// <summary>The number to put in that part.</summary>
    public float Number { get; init; }

    /// <summary>The name it gets, for <see cref="AttributeEditKind.Rename"/>.</summary>
    public string NewName { get; init; }

    /// <summary>Sets the attribute to a value on every selected node.</summary>
    public static AttributeEdit Set(string name, MemberValue value) =>
        new() { Kind = AttributeEditKind.Set, Name = name, Value = value, NewName = string.Empty };

    /// <summary>Replaces one number of the attribute's value.</summary>
    /// <param name="from">What a node that has no such value starts from.</param>
    public static AttributeEdit SetPart(string name, MemberValue from, int part, float number) =>
        new()
        {
            Kind = AttributeEditKind.SetPart, Name = name, Value = from, Part = part, Number = number,
            NewName = string.Empty,
        };

    /// <summary>Takes the attribute off every selected node.</summary>
    public static AttributeEdit Remove(string name) =>
        new() { Kind = AttributeEditKind.Remove, Name = name, NewName = string.Empty };

    /// <summary>Gives the attribute another name.</summary>
    public static AttributeEdit Rename(string name, string newName) =>
        new() { Kind = AttributeEditKind.Rename, Name = name, NewName = newName };
}
