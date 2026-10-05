using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// Every property the inspector can show. Declaration order is the panel's display order.
/// </summary>
public enum PropertyId
{
    /// <summary>Not a property; the default value of an unset row.</summary>
    None = 0,

    NodeName,
    NodeId,

    Position,
    Rotation,
    Scale,

    BrushKind,
    BrushOperation,
    BrushSize,

    LightKind,
    LightColor,
    LightIntensity,
    LightRange,
    LightEnabled,

    LightInnerAngle,
    LightOuterAngle,
    LightWidth,
    LightHeight,
    LightRadius,

    MeshModel,
    MeshSubmesh,

    /// <summary>Whether the node's geometry blocks what moves into it.</summary>
    CanCollide,

    /// <summary>Whether rays and overlap checks see the node's geometry.</summary>
    CanQuery,

    /// <summary>Whether the node raises touch events.</summary>
    CanTouch,

    /// <summary>Whether the node's own mesh or part brush is drawn.</summary>
    IsRendered,

    // Every keyvalue row shares EntityKeyvalue. Row identity is (Id, PropertyRow.Key).
    EntityClassname,
    EntityKeyvalue,

    /// <summary>Every face of a brush at once.</summary>
    BrushMaterial,

    /// <summary>Which face is picked, shown and not edited.</summary>
    FaceIndex,

    /// <summary>The picked face's material.</summary>
    FaceMaterial,

    /// <summary>Whether the picked face projects from the world or its own plane.</summary>
    FaceAlignment,

    /// <summary>World units per texture repeat across the picked face.</summary>
    FaceUScale,

    /// <summary>World units per texture repeat down the picked face.</summary>
    FaceVScale,

    /// <summary>Texture repeats of horizontal shift on the picked face.</summary>
    FaceUOffset,

    /// <summary>Texture repeats of vertical shift on the picked face.</summary>
    FaceVOffset,

    /// <summary>How far the picked face's texture is turned, in degrees.</summary>
    FaceRotation,
}

/// <summary>Which kind of file an asset row names.</summary>
public enum AssetKind
{
    /// <summary>Not an asset row.</summary>
    None,

    /// <summary>A <c>.spectramat</c>.</summary>
    Material,

    /// <summary>An image.</summary>
    Texture,

    /// <summary>A model file.</summary>
    Model,

    /// <summary>A sound.</summary>
    Sound,
}

/// <summary>How a row is edited. The panel renders one editor per kind.</summary>
public enum PropertyKind
{
    /// <summary>Shown, never edited: an id, a resolved asset path.</summary>
    ReadOnlyText,

    /// <summary>A single line of text.</summary>
    Text,

    /// <summary>One number.</summary>
    Number,

    /// <summary>Three numbers, labelled x, y and z.</summary>
    Vector3,

    /// <summary>Linear RGB, edited as three numbers.</summary>
    Color,

    /// <summary>A checkbox.</summary>
    Boolean,

    /// <summary>One of a fixed set of names.</summary>
    Choice,

    /// <summary>A path into the project's content, chosen from a picker.</summary>
    Asset,

    /// <summary>
    /// The name of an entity in this scene. Free text with a picker beside it,
    /// since wildcards and runtime tokens are legal values.
    /// </summary>
    Target,
}

/// <summary>
/// Which components of a three-number value a row or an edit refers to.
/// </summary>
[Flags]
public enum PropertyAxes
{
    None = 0,
    X = 1 << 0,
    Y = 1 << 1,
    Z = 1 << 2,
    All = X | Y | Z,
}

/// <summary>
/// One row of the inspector: what it is, which group it belongs to, and its
/// current value. Only the value field matching <see cref="Kind"/> is meaningful.
/// </summary>
// Crosses to the UI thread, so it holds no SceneNode, Brush or asset handle.
public readonly record struct PropertyRow
{
    /// <summary>The section this row is filed under: the payload the value came from.</summary>
    public string Group { get; init; }

    /// <summary>The label shown to the left of the editor.</summary>
    public string Name { get; init; }

    /// <summary>Which property this is, for applying an edit back.</summary>
    public PropertyId Id { get; init; }

    /// <summary>
    /// Which keyvalue or face this row is, for ids shared by several rows. Empty otherwise.
    /// Row identity is the pair (<see cref="Id"/>, <see cref="Key"/>).
    /// </summary>
    public string Key { get; init; }

    /// <summary>Which editor to render.</summary>
    public PropertyKind Kind { get; init; }

    /// <summary>The value, for <see cref="PropertyKind.Text"/>, <see cref="PropertyKind.ReadOnlyText"/> and <see cref="PropertyKind.Choice"/>.</summary>
    public string Text { get; init; }

    /// <summary>The value, for <see cref="PropertyKind.Number"/>.</summary>
    public float Number { get; init; }

    /// <summary>The value, for <see cref="PropertyKind.Vector3"/> and <see cref="PropertyKind.Color"/>.</summary>
    public Vector3 Vector { get; init; }

    /// <summary>The value, for <see cref="PropertyKind.Boolean"/>.</summary>
    public bool Flag { get; init; }

    /// <summary>The options, for <see cref="PropertyKind.Choice"/>. These are the stored tokens.</summary>
    // Shared arrays, not built per row: rows are refilled every publish.
    public IReadOnlyList<string> Choices { get; init; }

    /// <summary>
    /// The words shown for <see cref="Choices"/>, index for index. Defaults to the tokens.
    /// </summary>
    public IReadOnlyList<string> ChoiceLabels { get; init; }

    /// <summary>
    /// One line explaining what this row's choices mean, or empty.
    /// </summary>
    public string Help { get; init; }

    /// <summary>Which kind of content an <see cref="PropertyKind.Asset"/> row names.</summary>
    public AssetKind Asset { get; init; }

    /// <summary>
    /// A short state worth showing beside the value, or empty: <c>missing</c>
    /// for a path nothing resolves, <c>mixed (3 materials)</c> for a
    /// disagreement the value itself cannot express.
    /// </summary>
    public string Note { get; init; }

    /// <summary>
    /// How many of the selected nodes carry this property. An edit reaches only those.
    /// </summary>
    public int PresentCount { get; init; }

    /// <summary>How many nodes the selection held when this row was built.</summary>
    public int SelectionCount { get; init; }

    /// <summary>
    /// Which parts of the value differ across the nodes that carry it. Per axis for a
    /// three-number value; <see cref="PropertyAxes.All"/> or nothing for every other kind.
    /// </summary>
    public PropertyAxes MixedAxes { get; init; }

    /// <summary>Whether anything about this value differs across the selection.</summary>
    public bool IsMixed => MixedAxes != PropertyAxes.None;

    /// <summary>Whether only part of the selection carries this property.</summary>
    public bool IsPartial => PresentCount < SelectionCount;

    /// <summary>Whether this row can be edited at all.</summary>
    public bool IsEditable => Kind != PropertyKind.ReadOnlyText;

    /// <summary>
    /// The unit the value is measured in, or empty when it has none.
    /// </summary>
    public string Unit { get; init; }

    internal static PropertyRow ReadOnly(string group, string name, PropertyId id, string text, string key = "") =>
        new() { Group = group, Name = name, Id = id, Key = key, Kind = PropertyKind.ReadOnlyText, Text = text, Unit = "", Choices = [], PresentCount = 1, SelectionCount = 1 };

    internal static PropertyRow OfTarget(string group, string name, PropertyId id, string text, string key = "") =>
        new() { Group = group, Name = name, Id = id, Key = key, Kind = PropertyKind.Target, Text = text, Unit = "", Choices = [], PresentCount = 1, SelectionCount = 1 };

    internal static PropertyRow OfText(string group, string name, PropertyId id, string text, string key = "") =>
        new() { Group = group, Name = name, Id = id, Key = key, Kind = PropertyKind.Text, Text = text, Unit = "", Choices = [], PresentCount = 1, SelectionCount = 1 };

    internal static PropertyRow OfNumber(string group, string name, PropertyId id, float value, string unit = "", string key = "") =>
        new() { Group = group, Name = name, Id = id, Key = key, Kind = PropertyKind.Number, Number = value, Unit = unit, Choices = [], PresentCount = 1, SelectionCount = 1 };

    internal static PropertyRow OfVector(string group, string name, PropertyId id, Vector3 value, string unit = "", string key = "") =>
        new() { Group = group, Name = name, Id = id, Key = key, Kind = PropertyKind.Vector3, Vector = value, Unit = unit, Choices = [], PresentCount = 1, SelectionCount = 1 };

    internal static PropertyRow OfColor(string group, string name, PropertyId id, Vector3 value, string key = "") =>
        new() { Group = group, Name = name, Id = id, Key = key, Kind = PropertyKind.Color, Vector = value, Unit = "", Choices = [], PresentCount = 1, SelectionCount = 1 };

    internal static PropertyRow OfFlag(string group, string name, PropertyId id, bool value, string key = "") =>
        new() { Group = group, Name = name, Id = id, Key = key, Kind = PropertyKind.Boolean, Flag = value, Unit = "", Choices = [], PresentCount = 1, SelectionCount = 1 };

    internal static PropertyRow OfAsset(
        string group, string name, PropertyId id, string path, AssetKind kind,
        string note = "", string key = "") =>
        new()
        {
            Group = group, Name = name, Id = id, Key = key, Kind = PropertyKind.Asset,
            Text = path, Unit = "", Choices = [], Asset = kind, Note = note,
            PresentCount = 1, SelectionCount = 1,
        };

    internal static PropertyRow OfChoice(
        string group, string name, PropertyId id, string value, IReadOnlyList<string> choices,
        IReadOnlyList<string>? labels = null, string help = "", string key = "") =>
        new() { Group = group, Name = name, Id = id, Key = key, Kind = PropertyKind.Choice, Text = value, Unit = "", Choices = choices, ChoiceLabels = labels ?? choices, Help = help, PresentCount = 1, SelectionCount = 1 };
}
