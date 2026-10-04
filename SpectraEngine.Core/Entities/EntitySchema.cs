using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>How an entity class is placed in a level.</summary>
// Stored as one byte in a schema record. Append only.
public enum EntityPlacement : byte
{
    /// <summary>A point in space: the node carries a transform and nothing else.</summary>
    Point = 0,

    /// <summary>
    /// A volume: the node carries brush geometry the class gives behaviour to.
    /// </summary>
    Brush = 1,

    /// <summary>Logic only. Still a node, but its transform means nothing.</summary>
    Abstract = 2,
}

/// <summary>
/// Where an entity class was defined. An editor badge; nothing in the engine
/// branches on it.
/// </summary>
public enum EntityOrigin : byte
{
    /// <summary>Declared in the engine's own C#, through the entity attributes.</summary>
    EngineCSharp = 0,

    /// <summary>Declared by a game's Luau entity definition.</summary>
    Luau = 1,

    /// <summary>Declared by a game that references the engine as a library.</summary>
    SdkCSharp = 2,
}

/// <summary>
/// The widgets a <see cref="KeyvalueDescriptor"/> may ask for. An unknown value
/// is treated as <see cref="Auto"/>.
/// </summary>
// Byte constants, not an enum: the descriptor's field is the wire byte.
public static class KeyvalueWidget
{
    /// <summary>Let the editor choose from the declared <see cref="KeyvalueType"/>.</summary>
    public const byte Auto = 0;

    /// <summary>A slider, which is only meaningful with both a min and a max.</summary>
    public const byte Slider = 1;

    /// <summary>A browse-for-an-asset field.</summary>
    public const byte AssetPicker = 2;

    /// <summary>A pick-an-entity field.</summary>
    public const byte EntityPicker = 3;

    /// <summary>A swatch plus a hex field, over a linear colour.</summary>
    public const byte Color = 4;

    /// <summary>A checkbox per declared bit.</summary>
    public const byte Flags = 5;

    /// <summary>Whether <paramref name="widget"/> is one this vocabulary names.</summary>
    public static bool IsDefined(byte widget) => widget <= Flags;
}

/// <summary>
/// The bits of a <see cref="KeyvalueDescriptor.Flags"/> word that this engine
/// assigns meaning to.
/// </summary>
// Bits 3 to 7 are reserved for replication and per-property realm. Written
// zero, masked off on read.
public static class KeyvalueFlags
{
    /// <summary>Shown, never edited.</summary>
    public const uint ReadOnly = 1u << 0;

    /// <summary>Bound and carried, never shown.</summary>
    public const uint HideInEditor = 1u << 1;

    /// <summary>Takes effect on the next launch, and the editor says so.</summary>
    public const uint RequiresRestart = 1u << 2;

    /// <summary>The bits this engine understands. Everything else is masked off.</summary>
    public const uint DefinedMask = ReadOnly | HideInEditor | RequiresRestart;

    /// <summary>
    /// Drops every bit this engine does not assign meaning to. For reading a
    /// descriptor, not for writing one.
    /// </summary>
    public static uint Mask(uint flags) => flags & DefinedMask;
}

/// <summary>
/// One editable property of an entity class: its name, how it is presented, and
/// the bounds an editor enforces.
/// </summary>
/// <param name="Name">The wire name, as it appears in a map file.</param>
/// <param name="Display">The label an editor shows, or empty to use the name.</param>
/// <param name="Tooltip">One sentence of help, or empty.</param>
/// <param name="Default">The default value as map text. Compare as text.</param>
/// <param name="Type">What the value means.</param>
/// <param name="Widget">A <see cref="KeyvalueWidget"/> value.</param>
/// <param name="Min">Lower bound, or NaN for unbounded. Test with <see cref="HasMin"/>, not ==.</param>
/// <param name="Max">Upper bound, or NaN for unbounded. Test with <see cref="HasMax"/>, not ==.</param>
/// <param name="Flags">A <see cref="KeyvalueFlags"/> bit set.</param>
/// <param name="Choices">
/// The permitted values for <see cref="KeyvalueType.Choices"/>, empty otherwise.
/// </param>
public readonly record struct KeyvalueDescriptor(
    string Name,
    string Display,
    string Tooltip,
    string Default,
    KeyvalueType Type,
    byte Widget,
    float Min,
    float Max,
    uint Flags,
    IReadOnlyList<(string Value, string Display)> Choices)
{
    /// <summary>The choice list of every descriptor that declares no choices.</summary>
    public static readonly IReadOnlyList<(string Value, string Display)> NoChoices = [];

    /// <summary>Whether <see cref="Min"/> is a real bound.</summary>
    public bool HasMin => !float.IsNaN(Min);

    /// <summary>Whether <see cref="Max"/> is a real bound.</summary>
    public bool HasMax => !float.IsNaN(Max);

    /// <summary>Whether <see cref="KeyvalueFlags.ReadOnly"/> is set.</summary>
    public bool IsReadOnly => (Flags & KeyvalueFlags.ReadOnly) != 0;

    /// <summary>Whether <see cref="KeyvalueFlags.HideInEditor"/> is set.</summary>
    public bool IsHiddenInEditor => (Flags & KeyvalueFlags.HideInEditor) != 0;

    /// <summary>Whether <see cref="KeyvalueFlags.RequiresRestart"/> is set.</summary>
    public bool RequiresRestart => (Flags & KeyvalueFlags.RequiresRestart) != 0;
}

/// <summary>
/// Everything an editor and a runtime know about one entity class: its name, how
/// it is placed, the properties it exposes, and the inputs and outputs it wires.
/// The lists passed in are stored, not copied; do not mutate them afterwards.
/// </summary>
public sealed class EntitySchema
{
    public EntitySchema(
        string className,
        string displayName = "",
        string group = "",
        EntityPlacement placement = EntityPlacement.Point,
        EntityOrigin origin = EntityOrigin.EngineCSharp,
        IReadOnlyList<KeyvalueDescriptor>? keyvalues = null,
        IReadOnlyList<string>? inputs = null,
        IReadOnlyList<string>? outputs = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        ClassName = className;
        DisplayName = displayName;
        Group = group;
        Placement = placement;
        Origin = origin;
        Keyvalues = keyvalues ?? [];
        Inputs = inputs ?? [];
        Outputs = outputs ?? [];
    }

    /// <summary>The wire name, as a map file spells it.</summary>
    public string ClassName { get; }

    /// <summary>The label an editor shows, or empty to fall back to <see cref="ClassName"/>.</summary>
    public string DisplayName { get; }

    /// <summary>The category an editor files this class under, or empty.</summary>
    public string Group { get; }

    /// <summary>How the class is placed in a level.</summary>
    public EntityPlacement Placement { get; }

    /// <summary>Where the class was defined. A badge; nothing branches on it.</summary>
    public EntityOrigin Origin { get; }

    /// <summary>The properties the class exposes, in declaration order.</summary>
    public IReadOnlyList<KeyvalueDescriptor> Keyvalues { get; }

    /// <summary>The input names the class accepts.</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>The output names the class fires.</summary>
    public IReadOnlyList<string> Outputs { get; }
}
