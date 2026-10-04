namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// What a pack entry's payload is, as one byte in <see cref="PackEntry.Kind"/>.
/// </summary>
// The numbers are stored in packs: append-only.
// A routing hint only. Each cooked format still validates its own magic and version.
public enum PackEntryKind : byte
{
    /// <summary>Bytes with no cooked format of their own, served verbatim.</summary>
    Raw = 0,

    /// <summary>A cooked image: a restricted-profile KTX2 payload.</summary>
    Image = 1,

    /// <summary>A cooked mesh.</summary>
    Model = 2,

    /// <summary>Cooked audio.</summary>
    Audio = 3,

    /// <summary>A cooked material.</summary>
    Material = 4,

    /// <summary>A compiled shader blob, one per target backend.</summary>
    Shader = 5,

    /// <summary>Luau bytecode.</summary>
    Script = 6,

    /// <summary>A compiled map.</summary>
    Map = 7,

    /// <summary>Entity type definitions.</summary>
    EntityDefs = 8,

    /// <summary>Reserved: several small entries compressed as one solid block.</summary>
    Bundle = 9,

    /// <summary>Reserved: cooked video.</summary>
    Video = 10,

    /// <summary>
    /// A deletion with a zero-length payload. Lets a higher-priority pack remove
    /// content a lower-priority one shipped.
    /// </summary>
    Tombstone = 0xFF,
}
