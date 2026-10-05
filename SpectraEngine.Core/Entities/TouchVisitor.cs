namespace SpectraEngine.Core.Entities;

/// <summary>
/// Who is touching a sensor. Only the player visits in this version.
/// </summary>
public readonly struct TouchVisitor
{
    /// <summary>The id the player visits under.</summary>
    public const int PlayerId = 0;

    public TouchVisitor(int id, IPlayerPresence? player)
    {
        Id = id;
        Player = player;
    }

    /// <summary>Tells visitors apart. A sensing entity and an id are one touch.</summary>
    public int Id { get; }

    /// <summary>The player, when the player is the visitor. Null for anything else.</summary>
    public IPlayerPresence? Player { get; }
}
