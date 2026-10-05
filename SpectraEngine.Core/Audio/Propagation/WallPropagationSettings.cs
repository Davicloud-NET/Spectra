namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>The numbers <see cref="WallPropagation"/> runs on.</summary>
public sealed record WallPropagationSettings
{
    /// <summary>What the engine runs with.</summary>
    public static WallPropagationSettings Default { get; } = new();

    /// <summary>
    /// How many lines a sound is heard along: one to the listener and the
    /// rest to a ring round the listener's head. At least one.
    /// </summary>
    public int Lines { get; init; } = 5;

    /// <summary>The ring's radius, in units.</summary>
    // Narrower than the player's capsule at eye height, so no line ends in a
    // wall the player leans on. Its top is over the capsule's, under a low
    // ceiling, and a line that ends in a solid counts as the listener's own.
    public float HeadRadius { get; init; } = 0.25f;

    /// <summary>The most lines traced in one frame, over all sounds. At least <see cref="Lines"/>.</summary>
    public int TracesPerFrame { get; init; } = 60;

    /// <summary>
    /// An answer this many seconds old is traced again. A door that moves
    /// raises no signal, so this is how long a sound can stay as it was
    /// after its door shut.
    /// </summary>
    public float RefreshSeconds { get; init; } = 0.25f;

    /// <summary>
    /// An answer is traced again once the listener or the sound is this far
    /// from where it was traced, in units.
    /// </summary>
    // The gap between two lines of the ring, so a step this size takes an
    // edge across one line or two.
    public float MoveDistance { get; init; } = 0.1f;
}
