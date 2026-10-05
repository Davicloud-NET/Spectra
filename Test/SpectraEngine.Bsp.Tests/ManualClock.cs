namespace SpectraEngine.Bsp.Tests;

// A clock that moves only when a test moves it.
internal sealed class ManualClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => 1_000_000;

    public override long GetTimestamp() => _ticks;

    public void Advance(double seconds) => _ticks += (long)Math.Round(seconds * TimestampFrequency);
}
