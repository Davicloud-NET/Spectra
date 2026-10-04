namespace SpectraEngine.Core.Physics;

/// <summary>
/// The physics backend a host gets when it wires none: every call is a no-op.
/// Used by edit mode and headless tests.
/// </summary>
public sealed class NullScenePhysics : IScenePhysics
{
    /// <summary>The shared, stateless instance.</summary>
    public static NullScenePhysics Instance { get; } = new();

    private NullScenePhysics()
    {
    }

    /// <inheritdoc/>
    public bool IsSimulating => false;

    /// <inheritdoc/>
    public int BodyCount => 0;

    /// <inheritdoc/>
    public int StaticShapeCount => 0;

    /// <inheritdoc/>
    public void SyncStaticWorld(Scene.Scene scene)
    {
    }

    /// <inheritdoc/>
    public void PushKinematicTargets(float fixedDt)
    {
    }

    /// <inheritdoc/>
    public void Step(float fixedDt)
    {
    }

    /// <inheritdoc/>
    public void DrainEvents()
    {
    }

    /// <inheritdoc/>
    public void PublishRenderPoses(float alpha)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Shared instance, so this must stay a no-op.
    }
}
