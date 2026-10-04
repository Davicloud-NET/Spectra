using System;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Declares that a class is an entity type, and names the class a map file
/// spells to place one. Read by the source generator at compile time, never
/// at runtime.
/// </summary>
// The generator matches these by metadata name and reads the argument list, so
// an unset named argument is absent and gets inferred. Core must not reference
// the generator.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SpectraEntityAttribute : Attribute
{
    public SpectraEntityAttribute(string className) => ClassName = className;

    /// <summary>The wire name, as a map file spells it.</summary>
    public string ClassName { get; }

    /// <summary>The label an editor shows. Unset means "derive it from the class name".</summary>
    public string Display { get; set; } = "";

    /// <summary>The category an editor files this class under.</summary>
    public string Group { get; set; } = "";

    /// <summary>How instances are placed.</summary>
    public EntityPlacement Placement { get; set; } = EntityPlacement.Point;
}

/// <summary>
/// Declares a member as one of an entity's keyvalues, and names it as a map file
/// spells it.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class KeyvalueAttribute : Attribute
{
    public KeyvalueAttribute(string name) => Name = name;

    /// <summary>The wire name, as a map file spells it.</summary>
    public string Name { get; }

    /// <summary>The label an editor shows. Unset means "derive it from the name".</summary>
    public string Display { get; set; } = "";

    /// <summary>One sentence of help, shown beside the field.</summary>
    public string Tooltip { get; set; } = "";

    /// <summary>The value this keyvalue has when a map does not carry it, as text.</summary>
    public string Default { get; set; } = "";

    /// <summary>What the value means. Unset means "infer it from the member's own type".</summary>
    public KeyvalueType Type { get; set; }

    /// <summary>How an editor should present it. A <see cref="KeyvalueWidget"/> value.</summary>
    public byte Widget { get; set; } = KeyvalueWidget.Auto;

    /// <summary>Lower bound, or NaN (the default) for unbounded.</summary>
    public float Min { get; set; } = float.NaN;

    /// <summary>Upper bound, or NaN (the default) for unbounded.</summary>
    public float Max { get; set; } = float.NaN;
}

/// <summary>
/// Declares a method as an input another entity's output may be wired to. The
/// name is stated, so renaming the method does not break maps that wire it.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EntityInputAttribute : Attribute
{
    public EntityInputAttribute(string name) => Name = name;

    /// <summary>The wire name, as a map file spells it.</summary>
    public string Name { get; }
}

/// <summary>
/// Declares a member as an output other entities may be wired to. The member's
/// own name is the output's name.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EntityOutputAttribute : Attribute
{
}
