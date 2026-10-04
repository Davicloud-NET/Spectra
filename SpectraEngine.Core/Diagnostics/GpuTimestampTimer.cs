using System;

namespace SpectraEngine.Core.Diagnostics;

/// <summary>GPU time of the last completed sample, in milliseconds.</summary>
public readonly record struct GpuProfileSnapshot(bool Available, long Frame, double Total,
    double Shadows, double Geometry, double Lighting, double Resolve);

// A ring of async timestamp queries, shared by the three backends. When the
// ring is full the frame goes unmeasured; it never waits or reuses a live query.
internal abstract class GpuTimestampTimer : IDisposable
{
    protected const int Slots = 8, Marks = 64;
    private sealed class Frame
    {
        internal bool Pending;
        internal int Count;
        internal long Serial;
        internal readonly int[] Starts = new int[4];
        internal readonly (int Start, int End, int Phase)[] Pairs = new (int, int, int)[31];
        internal int PairCount;
    }
    private readonly Frame[] _frames = new Frame[Slots];
    private readonly ulong[] _values = new ulong[Marks];
    private readonly double[] _totals = new double[4];
    private int _active = -1, _cursor;
    private long _serial, _latest;
    protected int EndedSlot { get; private set; } = -1;
    public GpuProfileSnapshot Snapshot { get; private set; }
    public long DroppedFrames { get; private set; }

    protected GpuTimestampTimer()
    {
        for (int i = 0; i < Slots; i++) _frames[i] = new Frame();
    }

    public void BeginFrame()
    {
        _active = -1;
        EndedSlot = -1;
        _serial++;
        for (int slot = 0; slot < Slots; slot++)
        {
            Frame f = _frames[slot];
            if (!f.Pending || !TryRead(slot, f.Count, _values, out ulong frequency)) continue;
            f.Pending = false;
            if (f.Serial <= _latest) continue;
            _latest = f.Serial;
            bool valid = frequency != 0 && _values[1] >= _values[0];
            Array.Clear(_totals);
            for (int i = 0; i < f.PairCount; i++)
            {
                var pair = f.Pairs[i];
                valid &= _values[pair.End] >= _values[pair.Start];
                if (valid) _totals[pair.Phase] += (_values[pair.End] - _values[pair.Start]) * 1000.0 / frequency;
            }
            Snapshot = valid
                ? new(true, f.Serial, (_values[1] - _values[0]) * 1000.0 / frequency,
                    _totals[0], _totals[1], _totals[2], _totals[3])
                : new(false, f.Serial, 0, 0, 0, 0, 0);
        }
        for (int i = 0; i < Slots; i++)
        {
            int slot = (_cursor + i) % Slots;
            Frame f = _frames[slot];
            if (f.Pending) continue;
            _active = slot;
            _cursor = (slot + 1) % Slots;
            f.Count = 2;
            f.PairCount = 0;
            f.Serial = _serial;
            Array.Fill(f.Starts, -1);
            BeginQueries(slot);
            WriteTimestamp(slot, 0);
            return;
        }
        DroppedFrames++;
    }

    public void EndFrame()
    {
        if (_active < 0) return;
        Frame f = _frames[_active];
        WriteTimestamp(_active, 1);
        EndQueries(_active, f.Count);
        f.Pending = true;
        EndedSlot = _active;
        _active = -1;
    }

    public void Mark(FramePhase phase, bool begin)
    {
        int p = phase switch { FramePhase.Shadows => 0, FramePhase.Geometry => 1,
            FramePhase.Lighting => 2, FramePhase.Resolve => 3, _ => -1 };
        if (_active < 0 || p < 0) return;
        Frame f = _frames[_active];
        // Reserve two marks at open so a saturated frame cannot leave an unmatched pair.
        if (begin)
        {
            if (f.Count + 2 > Marks || f.Starts[p] >= 0) return;
            int start = f.Count;
            f.Count += 2;
            f.Starts[p] = start;
            WriteTimestamp(_active, start);
        }
        else if (f.Starts[p] is int start && start >= 0)
        {
            WriteTimestamp(_active, start + 1);
            f.Pairs[f.PairCount++] = (start, start + 1, p);
            f.Starts[p] = -1;
        }
    }

    protected virtual void BeginQueries(int slot) { }
    protected abstract void WriteTimestamp(int slot, int mark);
    protected abstract void EndQueries(int slot, int count);
    protected abstract bool TryRead(int slot, int count, ulong[] values, out ulong frequency);
    public abstract void Dispose();

    internal readonly struct FrameScope : IDisposable
    {
        private readonly GpuTimestampTimer? _timer;
        internal FrameScope(GpuTimestampTimer? timer) { _timer = timer; timer?.BeginFrame(); }
        public void Dispose() => _timer?.EndFrame();
    }
}
