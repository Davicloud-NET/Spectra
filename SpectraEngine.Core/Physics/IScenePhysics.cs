using System;

namespace SpectraEngine.Core.Physics;

/// <summary>
/// The engine's view of a physics backend: the calls the engine loop makes,
/// in the order it makes them. Render thread only.
/// </summary>
// Implementations live outside Core so Core needs no native physics library.
public interface IScenePhysics : IDisposable
{
    /// <summary>
    /// Whether this implementation simulates anything. False for
    /// <see cref="NullScenePhysics"/>.
    /// </summary>
    bool IsSimulating { get; }

    /// <summary>Bodies currently live in the physics world.</summary>
    int BodyCount { get; }

    /// <summary>Static collision shapes built from the compiled world.</summary>
    int StaticShapeCount { get; }

    /// <summary>
    /// Brings static collision in line with the compiled static world. Called
    /// once per frame where the compile result lands, not per tick.
    /// </summary>
    void SyncStaticWorld(Scene.Scene scene);

    /// <summary>
    /// Pushes this tick's target transforms for kinematic bodies. Call before
    /// <see cref="Step"/>.
    /// </summary>
    void PushKinematicTargets(float fixedDt);

    /// <summary>
    /// Advances the simulation by one fixed tick. <paramref name="fixedDt"/>
    /// is never a frame delta.
    /// </summary>
    void Step(float fixedDt);

    /// <summary>
    /// Drains the events the last <see cref="Step"/> produced: body moves,
    /// contacts, sensor overlaps. Call after every step, since the next step
    /// overwrites them.
    /// </summary>
    void DrainEvents();

    /// <summary>
    /// Publishes interpolated render poses for the frame, <paramref name="alpha"/>
    /// of the way from the previous tick to the current one.
    /// </summary>
    // Must not write through node transform setters: that dirties the spatial
    // index and shows scripts a display lerp instead of the simulated value.
    void PublishRenderPoses(float alpha);
}
