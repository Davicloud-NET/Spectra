using System;

namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// Declares a class a script holds objects of, such as <c>Instance</c> or
/// <c>RunService</c>. On a class that is not static, the members are its own.
/// On a static class, each member takes the object as its first parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ScriptClassAttribute : Attribute
{
    /// <summary>Declares the class under the name a script knows it by.</summary>
    public ScriptClassAttribute(string name) => Name = name;

    /// <summary>What a script calls the class.</summary>
    public string Name { get; }

    /// <summary>The class this one has every member of, or null for none.</summary>
    public string? Extends { get; set; }

    /// <summary>
    /// Type parameters, as Luau writes them: <c>T...</c> for what a signal
    /// hands its listeners. A class with any is written as a table type,
    /// since an extern type takes none.
    /// </summary>
    public string? TypeParameters { get; set; }

    /// <summary>
    /// What a name that is no member reads as to a type checker: the class
    /// of a child. Null when a script cannot name a child like a member.
    /// </summary>
    public string? ChildType { get; set; }
}
