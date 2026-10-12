using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Declares a property a script reads: of a class or a value, a constant of
/// a library, or a global. Its type is the member's own.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, Inherited = false)]
public sealed class ScriptPropertyAttribute : Attribute
{
    /// <summary>What a script calls it, when that is not the member's own name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The member of <see cref="ScriptWrites"/> a script's write goes
    /// through. A property that names none cannot be set.
    /// </summary>
    public SettableMemberId Sets { get; set; }

    /// <summary>
    /// The method a script's write goes through, for a property of an
    /// object that is no node. It is a method of the same class that takes
    /// the value and returns null, or why the value was refused.
    /// </summary>
    public string? SetBy { get; set; }

    /// <summary>
    /// What a script is told when it sets a property it cannot set, in
    /// place of the words for a read-only property.
    /// </summary>
    public string? CannotSet { get; set; }

    /// <summary>Its type as Luau writes it, when the member's own type does not say it.</summary>
    public string? Type { get; set; }

    /// <summary>The binding is written and installed by hand, and this only describes it.</summary>
    public bool ByHand { get; set; }
}
