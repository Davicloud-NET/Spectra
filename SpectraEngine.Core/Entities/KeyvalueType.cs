namespace SpectraEngine.Core.Entities;

/// <summary>
/// What a keyvalue means: what an editor renders, what a parser converts by, and
/// what a schema exporter writes as one byte.
/// </summary>
// A wire byte. Append only: inserting a member renumbers every kind after it
// in every schema already written.
public enum KeyvalueType : byte
{
    /// <summary>A flag. Written <c>"0"</c> or <c>"1"</c>, never <c>"true"</c>.</summary>
    Bool = 0,

    /// <summary>A whole number.</summary>
    Int = 1,

    /// <summary>A finite single-precision number.</summary>
    Float = 2,

    /// <summary>Free text, carried verbatim.</summary>
    String = 3,

    /// <summary>Two floats, <c>"x y"</c>.</summary>
    Vec2 = 4,

    /// <summary>Three floats, <c>"x y z"</c>.</summary>
    Vec3 = 5,

    /// <summary>Four floats, <c>"x y z w"</c>.</summary>
    Vec4 = 6,

    /// <summary>Three linear floats, <c>"r g b"</c>. Not sRGB and not a hex string.</summary>
    Color = 7,

    /// <summary>Three floats in degrees, <c>"pitch yaw roll"</c>.</summary>
    Angles = 8,

    /// <summary>The name of another entity, resolved at runtime.</summary>
    TargetName = 9,

    /// <summary>
    /// A reference to one node, by <c>SceneNode.Id</c>. Survives a rename.
    /// </summary>
    NodeRef = 10,

    /// <summary>A content-root-relative path to a model.</summary>
    AssetModel = 11,

    /// <summary>A content-root-relative path to a material.</summary>
    AssetMaterial = 12,

    /// <summary>A content-root-relative path to a texture.</summary>
    AssetTexture = 13,

    /// <summary>A content-root-relative path to a sound.</summary>
    AssetSound = 14,

    /// <summary>One value out of a declared list, written as that value's token.</summary>
    Choices = 15,

    /// <summary>A bit set, written as one non-negative integer.</summary>
    Flags = 16,

    /// <summary>
    /// A length in units from the node's position, written as one float. An
    /// editor draws it as a sphere round the node while the node is selected.
    /// </summary>
    Distance = 17,
}
