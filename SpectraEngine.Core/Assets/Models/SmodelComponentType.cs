namespace SpectraEngine.Core.Assets.Models;

/// <summary>
/// The element type of one vertex attribute's components. v1 cooks only
/// <see cref="Float32"/>; the reader passes through whatever the file declares.
/// </summary>
public enum SmodelComponentType : byte
{
    /// <summary>IEEE 754 binary32. The only type v1 cooks.</summary>
    Float32 = 0,

    /// <summary>Unsigned 8-bit integer. Reserved for blend indices.</summary>
    UInt8 = 1,

    /// <summary>Unsigned 16-bit integer. Reserved for blend indices.</summary>
    UInt16 = 2,

    /// <summary>Unsigned 32-bit integer. Reserved.</summary>
    UInt32 = 3,
}
