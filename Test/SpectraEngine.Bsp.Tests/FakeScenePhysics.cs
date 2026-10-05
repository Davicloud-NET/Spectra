using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// Simulates nothing. Logs one line per tick call, into a list a test can share
// with other recorders to assert the order across them.
internal sealed class FakeScenePhysics : IScenePhysics
{
    private readonly List<string> _log;

    public FakeScenePhysics(List<string>? log = null) => _log = log ?? [];

    public int Steps { get; private set; }

    public bool IsSimulating => false;

    public int BodyCount => 0;

    public int StaticShapeCount => 0;

    public void SyncStaticWorld(Scene scene)
    {
    }

    public void PushKinematicTargets(float fixedDt) => _log.Add("push");

    public void Step(float fixedDt)
    {
        Steps++;
        _log.Add("step");
    }

    public void DrainEvents() => _log.Add("drain");

    public void PublishRenderPoses(float alpha)
    {
    }

    public void Dispose()
    {
    }
}
