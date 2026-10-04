namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// What one vertex attribute in a <c>.smodel</c> means.
/// </summary>
// Append-only: the values are stored per attribute and hashed into the layout id.
public enum SmodelSemantic : byte
{
    /// <summary>Object-space position.</summary>
    Position = 0,

    /// <summary>Object-space normal.</summary>
    Normal = 1,

    /// <summary>Tangent with the bitangent sign in <c>w</c>.</summary>
    Tangent4 = 2,

    /// <summary>The first texture coordinate set.</summary>
    Uv0 = 3,

    /// <summary>The second texture coordinate set, typically a lightmap.</summary>
    Uv1 = 4,

    /// <summary>The first vertex colour set.</summary>
    Color0 = 5,

    /// <summary>Joint indices for skinning.</summary>
    BlendIndices = 6,

    /// <summary>Joint weights for skinning.</summary>
    BlendWeights = 7,
}
