namespace SpectraEngine.Core.Entities;

/// <summary>The shapes a <see cref="ParameterValue"/> comes in.</summary>
public enum ParameterValueKind : byte
{
    /// <summary>No value.</summary>
    None = 0,

    /// <summary>True or false.</summary>
    Boolean,

    /// <summary>One number.</summary>
    Number,

    /// <summary>Text. The name of another entity is text too.</summary>
    Text,

    /// <summary>Two, three or four numbers.</summary>
    Vector,

    /// <summary>One node, by its id.</summary>
    NodeId,
}
