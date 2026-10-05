using System;

namespace SpectraEngine.Core.Audio.Propagation;

// The answers kept from one frame to the next. A sound that is asked about
// again finds its answer by its body. One that is not asked about loses it,
// so it counts as new when it comes back.
internal sealed class WallAnswers
{
    private WallAnswer[] _kept = [];
    private WallAnswer[] _last = [];
    private int _lastCount;

    // Sounds come in the same order every frame, so the search for the next
    // one starts where the last one was found.
    private int _next;

    public int Count { get; private set; }

    public ref WallAnswer this[int slot] => ref _kept[slot];

    // Starts a frame that asks about at most this many sounds.
    public void Begin(int sounds)
    {
        (_kept, _last) = (_last, _kept);
        _lastCount = Count;
        Count = 0;
        _next = 0;

        if (_kept.Length < sounds)
            _kept = new WallAnswer[Math.Max(sounds, Math.Max(16, _kept.Length * 2))];
    }

    // The slot of the answer for a sound on this body: last frame's when it
    // had one, and otherwise an empty one.
    public int Claim(Guid body)
    {
        int slot = Count++;

        for (int i = _next; i < _lastCount; i++)
        {
            if (_last[i].Body != body)
                continue;

            _kept[slot] = _last[i];
            _next = i + 1;
            return slot;
        }

        _kept[slot] = new WallAnswer { Body = body };
        return slot;
    }

    public void Clear()
    {
        Count = 0;
        _lastCount = 0;
        _next = 0;
    }
}
