using System;

namespace SpectraEngine.Core.Physics;

/// <summary>
/// Turns a variable frame delta into a whole number of fixed simulation ticks,
/// plus the fraction of a tick left over for render interpolation.
/// Render-thread only.
/// </summary>
// Ticks per frame are capped. When a frame owes more, the excess time is
// dropped and counted: carrying it makes each frame longer than the last.
public sealed class FixedTickAccumulator
{
    private readonly float _fixedDt;
    private readonly int _maxTicksPerFrame;
    // Double: float would drift over a long session.
    private double _accumulated;

    /// <summary>Creates an accumulator at the engine defaults.</summary>
    public FixedTickAccumulator()
        : this(PhysicsDefaults.FixedDeltaTime, PhysicsDefaults.MaxTicksPerFrame)
    {
    }

    /// <summary>Creates an accumulator with an explicit step and cap.</summary>
    public FixedTickAccumulator(float fixedDt, int maxTicksPerFrame)
    {
        _fixedDt = fixedDt > 0f ? fixedDt : PhysicsDefaults.FixedDeltaTime;
        _maxTicksPerFrame = maxTicksPerFrame > 0 ? maxTicksPerFrame : 1;
    }

    /// <summary>The fixed step, in seconds, every tick advances by.</summary>
    public float FixedDeltaTime => _fixedDt;

    /// <summary>Ticks run since construction.</summary>
    public long TotalTicks { get; private set; }

    /// <summary>
    /// Ticks the cap discarded since construction. Non-zero means the machine
    /// could not keep up and simulated time was lost.
    /// </summary>
    public long DroppedTicks { get; private set; }

    /// <summary>
    /// How far the current frame sits between the last completed tick and the
    /// next one, in <c>[0, 1)</c>.
    /// </summary>
    public float Alpha
    {
        get
        {
            // Narrowing to float can round up to 1f. Clamp to keep [0, 1).
            float alpha = (float)(_accumulated / _fixedDt);
            if (alpha >= 1f)
                return MathF.BitDecrement(1f);
            return alpha < 0f ? 0f : alpha;
        }
    }

    /// <summary>
    /// Adds a frame's elapsed time and returns how many fixed ticks to run now.
    /// A non-finite or negative delta is ignored.
    /// </summary>
    public int Advance(double deltaTime)
    {
        if (!double.IsFinite(deltaTime) || deltaTime <= 0d)
            return 0;

        _accumulated += deltaTime;

        int ticks = (int)(_accumulated / _fixedDt);
        if (ticks <= 0)
            return 0;

        if (ticks > _maxTicksPerFrame)
        {
            DroppedTicks += ticks - _maxTicksPerFrame;
            ticks = _maxTicksPerFrame;
            _accumulated = 0d;
        }
        else
        {
            _accumulated -= ticks * (double)_fixedDt;
        }

        TotalTicks += ticks;
        return ticks;
    }

    /// <summary>
    /// Discards the partial tick, for a scene change or a resume.
    /// </summary>
    public void Reset() => _accumulated = 0d;
}
