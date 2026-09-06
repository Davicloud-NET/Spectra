using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// Exclusive, synchronous compile scratch. Only collections borrowed here are
/// recycled; published arrays, grids, and mesh deltas always own their storage.
/// No references to a job survive Dispose. Large edits release their capacity.
/// </summary>
internal sealed class CompileWorkspace : IDisposable
{
    internal const int RetentionLimit = 4096;
    [ThreadStatic] private static CompileWorkspace? _available;
    private readonly List<Buffer?> _buffers = [];
    private int _cursor;
    private bool _leased;

    internal static CompileWorkspace Rent()
    {
        var workspace = _available ?? new();
        _available = null;
        workspace._leased = true;
        return workspace;
    }

    internal List<T> List<T>(IEnumerable<T>? source = null)
    {
        var list = Take<ListBuffer<T>>().Value;
        if (source is not null) list.AddRange(source);
        return list;
    }
    internal HashSet<T> Set<T>(IEnumerable<T>? source = null)
    {
        var set = Take<SetBuffer<T>>().Value;
        if (source is not null) set.UnionWith(source);
        return set;
    }
    internal Dictionary<TKey, TValue> Map<TKey, TValue>() where TKey : notnull => Take<MapBuffer<TKey, TValue>>().Value;

    private T Take<T>() where T : Buffer, new()
    {
        if (!_leased) throw new ObjectDisposedException(nameof(CompileWorkspace));
        if (_cursor == _buffers.Count) _buffers.Add(null);
        if (_buffers[_cursor] is not T value) _buffers[_cursor] = value = new T();
        _cursor++;
        return value;
    }
    public void Dispose()
    {
        if (!_leased) return;
        _leased = false;
        foreach (Buffer? buffer in _buffers) buffer?.Clear();
        for (int i = 0; i < _buffers.Count; i++)
            if (_buffers[i]?.Capacity > RetentionLimit) _buffers[i] = null;
        _cursor = 0;
        if (_buffers.Count <= RetentionLimit) _available ??= this;
    }

    private abstract class Buffer { public abstract int Capacity { get; } public abstract void Clear(); }
    private sealed class ListBuffer<T> : Buffer
    {
        public readonly List<T> Value = [];
        public override int Capacity => Value.Capacity;
        public override void Clear() => Value.Clear();
    }
    private sealed class SetBuffer<T> : Buffer
    {
        public readonly HashSet<T> Value = [];
        public override int Capacity => Value.EnsureCapacity(0);
        public override void Clear() => Value.Clear();
    }
    private sealed class MapBuffer<TKey, TValue> : Buffer where TKey : notnull
    {
        public readonly Dictionary<TKey, TValue> Value = [];
        public override int Capacity => Value.EnsureCapacity(0);
        public override void Clear() => Value.Clear();
    }
}
