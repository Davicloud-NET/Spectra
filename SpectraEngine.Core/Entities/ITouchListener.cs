namespace SpectraEngine.Core.Entities;

/// <summary>
/// Implemented by an entity that senses with its brushes. It hears nothing
/// until it has called <see cref="TouchTracker.Register"/>.
/// </summary>
public interface ITouchListener
{
    /// <summary>A visitor came inside one of the entity's brushes.</summary>
    void OnTouchStarted(in TouchVisitor visitor);

    /// <summary>
    /// A visitor that was inside is clear of every brush, or the entity
    /// stopped sensing or was despawned. Called once for each start, except
    /// when the world deactivates: then nothing is called.
    /// </summary>
    void OnTouchEnded(in TouchVisitor visitor);
}
