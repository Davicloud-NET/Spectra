using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using System;
using System.Numerics;

namespace SpectraEngine.Core.Audio;

// Gives each sound of a running level its Doppler factor for the frame.
// The level is told nothing: only the pitch on the device bends.
// Render thread only.
internal sealed class LevelDoppler
{
    private readonly SoundSimulationSwitches _switches;

    private DopplerClock _clock;
    private Vector3 _listener;

    // The level's tick at the last frame. Means nothing unless _hasTick.
    private long _tick;
    private bool _hasTick;

    public LevelDoppler(SoundSimulationSwitches switches) => _switches = switches;

    // Once a frame, before any sound is stepped.
    public void BeginFrame(EntityWorld world, Vector3 listener, float deltaSeconds)
    {
        long tick = world.TickNumber;

        // The first frame of a level has no tick to count from. Taken as a
        // frame whose ticks covered it.
        float tickSeconds = _hasTick ? Math.Max(tick - _tick, 0L) * world.FixedDeltaTime : deltaSeconds;

        _tick = tick;
        _hasTick = true;
        _listener = listener;
        _clock.Advance(deltaSeconds, tickSeconds);
    }

    // For a sound that was asked about this frame, once its place is known.
    public void Step(ref PresentedEmitter presented, bool listenerJumped)
    {
        // A sound that plays at the listener has no path to get longer.
        if (!presented.IsPlaced || (presented.Simulated & SoundSimulation.Doppler) == 0)
        {
            presented.Doppler.Reset();
            return;
        }

        // A path has no length of its own, only the place the sound seems to
        // come from. The straight line from there to the listener stands in
        // for it, which is right until a path bends.
        float length = Vector3.Distance(presented.Position, _listener);

        presented.Doppler.Step(length, in _clock, listenerJumped, _switches.DopplerStrength);
    }

    // For the end of a level.
    public void Forget()
    {
        _hasTick = false;
        _clock.Reset();
    }
}
