using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// The fixed set of AL sources the engine owns. A new sound gets a free
/// source, then the oldest finished one, then steals the oldest one-shot.
/// Render thread only.
/// </summary>
// Streaming entries are never stolen and never judged by AL state: a starved
// stream reports Stopped just like a finished sound.
public sealed class AudioSourcePool
{
    private enum EntryKind
    {
        Free,
        OneShot,
        Streaming,
    }

    private struct Entry
    {
        public uint Source;
        public EntryKind Kind;

        // Acquire order.
        public long Sequence;
    }

    private readonly IAudioBackend _backend;
    private readonly Entry[] _entries;
    private long _sequence;

    /// <summary>
    /// Creates up to <paramref name="requestedCount"/> sources, stopping at the
    /// first refusal. <see cref="Capacity"/> reports what was granted.
    /// </summary>
    public AudioSourcePool(IAudioBackend backend, int requestedCount)
    {
        if (requestedCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedCount), requestedCount, "A pool needs at least one source.");

        _backend = backend;

        var created = new List<Entry>(requestedCount);
        for (int i = 0; i < requestedCount; i++)
        {
            if (!backend.TryCreateSource(out uint source))
                break;

            created.Add(new Entry { Source = source, Kind = EntryKind.Free, Sequence = 0 });
        }

        _entries = created.ToArray();
    }

    /// <summary>Sources the driver actually granted.</summary>
    public int Capacity => _entries.Length;

    /// <summary>Entries currently held by a voice.</summary>
    public int InUse
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Kind != EntryKind.Free) count++;
            return count;
        }
    }

    /// <summary>
    /// Sources taken from a sound that was still playing. Non-zero means the
    /// pool is too small for the scene.
    /// </summary>
    public int StolenCount { get; private set; }

    /// <summary>Sounds dropped because every source was carrying a streaming voice.</summary>
    public int StarvedCount { get; private set; }

    /// <summary>
    /// Hands out a source: a free one, else the oldest finished one-shot, else
    /// the oldest playing one-shot. False when every source is streaming.
    /// </summary>
    /// <param name="streaming">True exempts the entry from reclaim and steal.</param>
    public bool TryAcquire(bool streaming, out uint source)
    {
        int index = FindFree();

        if (index < 0) index = FindOldest(onlyFinished: true);

        if (index < 0)
        {
            index = FindOldest(onlyFinished: false);
            if (index >= 0) StolenCount++;
        }

        if (index < 0)
        {
            // All streaming. Drop the new sound instead of cutting off music.
            StarvedCount++;
            source = 0;
            return false;
        }

        ref Entry entry = ref _entries[index];
        if (entry.Kind != EntryKind.Free)
        {
            _backend.Stop(entry.Source);

            // AL refuses to queue on a source that still holds a static buffer.
            _backend.SetSourceBuffer(entry.Source, 0);
        }

        entry.Kind = streaming ? EntryKind.Streaming : EntryKind.OneShot;
        entry.Sequence = ++_sequence;
        source = entry.Source;
        return true;
    }

    /// <summary>
    /// Returns a source to the pool. Handles are reused, so releasing one the
    /// caller no longer holds frees whatever sound has it now.
    /// </summary>
    public void Release(uint source)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Source != source || _entries[i].Kind == EntryKind.Free)
                continue;

            _backend.Stop(source);
            _backend.SetSourceBuffer(source, 0);
            _entries[i].Kind = EntryKind.Free;
            _entries[i].Sequence = 0;
            return;
        }
    }

    /// <summary>Stops everything and frees every entry.</summary>
    public void ReleaseAll()
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Kind == EntryKind.Free) continue;
            _backend.Stop(_entries[i].Source);
            _entries[i].Kind = EntryKind.Free;
            _entries[i].Sequence = 0;
        }
    }

    /// <summary>Destroys every source. The pool is unusable afterwards.</summary>
    public void Dispose()
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            _backend.Stop(_entries[i].Source);
            _backend.DestroySource(_entries[i].Source);
            _entries[i].Kind = EntryKind.Free;
        }
    }

    private int FindFree()
    {
        for (int i = 0; i < _entries.Length; i++)
            if (_entries[i].Kind == EntryKind.Free) return i;
        return -1;
    }

    private int FindOldest(bool onlyFinished)
    {
        int best = -1;
        long bestSequence = long.MaxValue;

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Kind != EntryKind.OneShot) continue;

            if (onlyFinished)
            {
                AudioSourceState state = _backend.GetSourceState(_entries[i].Source);
                if (state is AudioSourceState.Playing or AudioSourceState.Paused) continue;
            }

            if (_entries[i].Sequence >= bestSequence) continue;
            bestSequence = _entries[i].Sequence;
            best = i;
        }

        return best;
    }
}
