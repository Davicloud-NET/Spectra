namespace SpectraEngine.Core.Entities;

/// <summary>
/// One wire from an entity's output to another entity's input: when
/// <see cref="Output"/> fires, every entity named <see cref="TargetName"/> is
/// sent <see cref="Input"/> with <see cref="Parameter"/>, after
/// <see cref="Delay"/> seconds.
/// </summary>
/// <param name="Output">The name of the output that fires this wire.</param>
/// <param name="TargetName">
/// The name of the entity or entities to send to, resolved when the output fires.
/// </param>
/// <param name="Input">The name of the input to send.</param>
/// <param name="Parameter">The argument to send, empty for none.</param>
/// <param name="Delay">Seconds to wait before sending. Zero fires on the same tick.</param>
/// <param name="TimesToFire">
/// How many times this wire may fire before it is removed, or
/// <see cref="Infinite"/> for no limit.
/// </param>
public readonly record struct EntityConnection(
    string Output,
    string TargetName,
    string Input,
    string Parameter,
    float Delay,
    int TimesToFire)
{
    /// <summary>
    /// The <see cref="TimesToFire"/> value meaning no limit. Any negative value
    /// reads the same way.
    /// </summary>
    public const int Infinite = -1;

    /// <summary>Whether this wire has no firing limit.</summary>
    public bool FiresForever => TimesToFire < 0;
}
