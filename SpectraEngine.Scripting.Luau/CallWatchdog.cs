using System.Diagnostics;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

// Stops a call into Luau that runs past the limit. The thread that runs the
// scripts reads no clock for this: the shim numbers its outermost calls, and
// the thread here looks at the number a few times within the limit. A call
// it sees twice with the same number has run for that long.
internal sealed class CallWatchdog
{
    private const int LooksPerLimit = 8;

    private static readonly TimeSpan _longestWait = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _shortestWait = TimeSpan.FromMilliseconds(1);

    // Held for every look at the state, and to stop. So no look comes after
    // Stop has returned, and the state can be closed.
    private readonly Lock _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly nint _state;
    private Thread? _thread;
    private long _limitTicks;
    private bool _isStopped;

    internal CallWatchdog(nint state, TimeSpan limit)
    {
        _state = state;
        _limitTicks = limit.Ticks;
    }

    // Zero or less, or Timeout.InfiniteTimeSpan, turns it off.
    internal TimeSpan Limit
    {
        get => new(Interlocked.Read(ref _limitTicks));
        set
        {
            Interlocked.Exchange(ref _limitTicks, value.Ticks);
            lock (_gate)
            {
                if (!_isStopped)
                    _wake.Set();
            }
        }
    }

    internal void Start()
    {
        lock (_gate)
        {
            if (_isStopped || _thread is not null)
                return;

            _thread = new Thread(Run) { IsBackground = true, Name = "Luau watchdog" };
            _thread.Start();
        }
    }

    internal void Stop()
    {
        lock (_gate)
        {
            if (_isStopped)
                return;

            _isStopped = true;
            if (_thread is null)
                _wake.Dispose();
            else
                _wake.Set();
        }
    }

    private void Run()
    {
        uint watched = 0;
        long since = 0;

        while (true)
        {
            TimeSpan limit = Limit;
            _wake.WaitOne(limit > TimeSpan.Zero ? WaitFor(limit) : Timeout.InfiniteTimeSpan);

            lock (_gate)
            {
                if (_isStopped)
                    break;

                limit = Limit;
                ulong call = Shim.sl_callstate(_state);
                uint serial = (uint)(call >> 1);
                bool isRunning = (call & 1) != 0;

                if (!isRunning || limit <= TimeSpan.Zero)
                    watched = 0;
                else if (serial != watched)
                {
                    watched = serial;
                    since = Stopwatch.GetTimestamp();
                }
                else if (Stopwatch.GetElapsedTime(since) >= limit)
                    Shim.sl_interrupt_arm(_state, serial);
            }
        }

        _wake.Dispose();
    }

    private static TimeSpan WaitFor(TimeSpan limit)
    {
        TimeSpan wait = limit / LooksPerLimit;
        return wait > _longestWait ? _longestWait : wait < _shortestWait ? _shortestWait : wait;
    }
}
