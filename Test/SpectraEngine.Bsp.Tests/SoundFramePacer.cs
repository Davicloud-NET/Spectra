namespace SpectraEngine.Bsp.Tests;

// Runs a presenter rig as the engine runs a level: ticks of a sixtieth of a
// second, and frames at a rate of their own.
internal sealed class SoundFramePacer
{
    private const double TickSeconds = 1.0 / 60.0;

    private readonly SoundPresenterRig _rig;
    private readonly double _frameSeconds;
    private double _owed;

    public SoundFramePacer(SoundPresenterRig rig, int framesPerSecond)
    {
        _rig = rig;
        _frameSeconds = 1.0 / framesPerSecond;
    }

    // Called once for every tick, with the seconds the ticks have covered.
    // What moves here moves as an entity does.
    public Action<double>? OnTick { get; set; }

    // Called once a frame after its ticks, with the seconds the frames have
    // covered. What moves here moves as the camera does.
    public Action<double>? OnFrame { get; set; }

    // Called at the end of every frame, once the presenter has run.
    public Action? AfterFrame { get; set; }

    public long Ticks { get; private set; }

    public double FrameSeconds { get; private set; }

    public void Frame()
    {
        FrameSeconds += _frameSeconds;
        _owed += _frameSeconds;

        int ticks = (int)(_owed / TickSeconds);
        _owed -= ticks * TickSeconds;

        for (int i = 0; i < ticks; i++)
        {
            Ticks++;
            OnTick?.Invoke(Ticks * TickSeconds);
            _rig.World.Tick(SoundPresenterRig.TickSeconds);
        }

        OnFrame?.Invoke(FrameSeconds);
        _rig.Audio.Update();
        _rig.Presenter.Update(_rig.World, (float)_frameSeconds);
        AfterFrame?.Invoke();
    }

    public void Run(double seconds)
    {
        double until = FrameSeconds + seconds;
        while (FrameSeconds < until)
            Frame();
    }
}
