using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

// An entity that moves its own node: up for some ticks and down again, for
// as long as the level runs. Whatever a script wrote to the node between
// two ticks, the next tick puts it on the entity's path again.
internal sealed class PacingEntity : Entity
{
    public const string Class = "pacer";

    private const int TicksUp = 8;
    private const float Step = 0.125f;

    private Transform _home;
    private int _ticks;

    protected internal override void OnSpawn()
    {
        _home = Node.LocalTransform;
        SetTicking(true);
    }

    protected internal override void OnTick()
    {
        _ticks++;
        int along = _ticks % (2 * TicksUp);
        int stepsUp = along <= TicksUp ? along : (2 * TicksUp) - along;

        Transform pose = _home with { Position = _home.Position + new Vector3(0f, stepsUp * Step, 0f) };
        MoveNode(in pose);
    }
}
