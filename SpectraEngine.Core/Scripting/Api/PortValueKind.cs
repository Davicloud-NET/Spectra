namespace SpectraEngine.Core.Scripting.Api;

/// <summary>
/// What the one value of an input or an output is to a script. A wire
/// carries it as text, and <see cref="EntityClassApi.KindOf"/> says which of
/// these the text of a type is.
/// </summary>
public enum PortValueKind : byte
{
    /// <summary>True or false.</summary>
    Boolean,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A number with no fraction, as a counter's step is.</summary>
    WholeNumber,

    /// <summary>A string. The name of another entity is one too.</summary>
    Text,

    /// <summary>A Vector3: a position, angles in degrees, or a colour's red, green and blue.</summary>
    Vector3,

    /// <summary>Two numbers, each a value of its own.</summary>
    TwoNumbers,

    /// <summary>Four numbers, each a value of its own.</summary>
    FourNumbers,

    /// <summary>A node, or nil for none.</summary>
    Node,
}
