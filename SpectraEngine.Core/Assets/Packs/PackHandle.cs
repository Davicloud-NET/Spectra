using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// The refcounted lifetime of one mounted pack. A span into the mapped view is
/// only valid while its holder has a reference.
/// </summary>
// Unmapping under a live span is an access violation, not an exception.
// The mount holds one reference and every ContentBlob from the pack holds another;
// storage is released at zero. No finalizer: mount and unmount are explicit.
public sealed unsafe class PackHandle
{
    private readonly object _gate = new();

    private MemoryMappedViewAccessor? _view;
    private MemoryMappedFile? _mapping;
    private IDisposable? _storage;
    private byte* _origin;
    private bool _pointerAcquired;

    private int _references = 1;
    private bool _unmountRequested;

    private PackHandle(string name, long regionLength)
    {
        Name = name;
        RegionLength = regionLength;
    }

    /// <summary>The pack's name, for log lines and exception messages.</summary>
    public string Name { get; }

    /// <summary>Bytes in the mapped region, which is the whole file.</summary>
    public long RegionLength { get; }

    /// <summary>
    /// Whether a mapped view is still present. False for a handle over a stream,
    /// which has no view, and false once the last reference has gone.
    /// </summary>
    public bool IsMapped => _origin is not null;

    /// <summary>References outstanding, the mount's own included.</summary>
    public int ReferenceCount
    {
        get { lock (_gate) return _references; }
    }

    /// <summary>Whether the storage has been released.</summary>
    public bool IsReleased
    {
        get { lock (_gate) return _references == 0; }
    }

    /// <summary>
    /// Whether <see cref="RequestUnmount"/> has been called. The storage may
    /// still be alive: an unmount waits for the last reference.
    /// </summary>
    public bool UnmountRequested
    {
        get { lock (_gate) return _unmountRequested; }
    }

    // One read-only view of the whole file, not a view per entry: Windows view
    // offsets must be multiples of 64 KB.
    internal static PackHandle MapWholeFile(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = stream.Length;

        MemoryMappedFile? mapping = null;
        MemoryMappedViewAccessor? view = null;
        try
        {
            // Before the mapping: a zero-length file cannot be mapped.
            PackFormat.RequireMinimumFileSize(path, length);

            mapping = MemoryMappedFile.CreateFromFile(
                stream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read,
                HandleInheritability.None, leaveOpen: false);

            view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            byte* origin = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref origin);

            var handle = new PackHandle(path, length)
            {
                _mapping = mapping,
                _view = view,
                // PointerOffset is zero for a view at offset 0. Added anyway, per the API contract.
                _origin = origin + view.PointerOffset,
                _pointerAcquired = true,
            };

            return handle;
        }
        catch
        {
            view?.Dispose();
            mapping?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    // A handle over the stream fallback's file instead of a mapping. Its blobs are
    // copies but still hold a reference, so both sources follow one rule.
    internal static PackHandle OverStorage(string name, IDisposable storage, long length)
    {
        ArgumentNullException.ThrowIfNull(storage);
        return new PackHandle(name, length) { _storage = storage };
    }

    /// <summary>
    /// Takes a reference, or returns false when the pack is unmounting or
    /// already gone.
    /// </summary>
    // Refusing after an unmount request is what lets a busy pack reach zero.
    public bool TryAddRef()
    {
        lock (_gate)
        {
            if (_unmountRequested || _references == 0) return false;

            _references++;
            return true;
        }
    }

    /// <summary>Takes a reference, or throws when the pack is unmounting or gone.</summary>
    /// <exception cref="ObjectDisposedException">The pack is unmounting or unmounted.</exception>
    public void AddRef()
    {
        if (TryAddRef()) return;

        throw new ObjectDisposedException(
            nameof(PackHandle), $"Pack '{Name}' is unmounted, so no further reference can be taken.");
    }

    /// <summary>
    /// Drops one reference, releasing the storage when it was the last.
    /// </summary>
    /// <exception cref="InvalidOperationException">Released more times than referenced.</exception>
    public void Release()
    {
        bool last;
        lock (_gate)
        {
            if (_references == 0)
            {
                throw new InvalidOperationException(
                    $"Pack '{Name}' was released more times than it was referenced.");
            }

            last = --_references == 0;
        }

        if (last) ReleaseStorage();
    }

    /// <summary>
    /// The bytes at <paramref name="offset"/>. Valid only while the caller holds
    /// a reference.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The storage is already gone.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The window leaves the region.</exception>
    public ReadOnlySpan<byte> Slice(ulong offset, int length)
    {
        // No lock: the caller's reference keeps the pointer valid.
        byte* origin = _origin;
        if (origin is null)
        {
            throw new ObjectDisposedException(
                nameof(PackHandle), $"Pack '{Name}' has no mapped view to read from.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ulong region = (ulong)RegionLength;
        if (offset > region || (ulong)length > region - offset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                $"A window of {length} bytes at {offset} leaves pack '{Name}', which is {RegionLength} bytes.");
        }

        return new ReadOnlySpan<byte>(origin + offset, length);
    }

    // Drops the mount's own reference. Storage goes when the last blob does. Idempotent.
    internal void RequestUnmount()
    {
        bool last;
        lock (_gate)
        {
            if (_unmountRequested) return;

            _unmountRequested = true;
            last = --_references == 0;
        }

        if (last) ReleaseStorage();
    }

    /// <inheritdoc/>
    public override string ToString() => Name;

    // Runs once: only one decrement can reach zero.
    private void ReleaseStorage()
    {
        // Nulled first so a racing Slice throws instead of reading unmapped memory.
        _origin = null;

        if (_pointerAcquired)
        {
            _pointerAcquired = false;
            _view!.SafeMemoryMappedViewHandle.ReleasePointer();
        }

        _view?.Dispose();
        _view = null;

        // Also closes the FileStream (leaveOpen: false).
        _mapping?.Dispose();
        _mapping = null;

        _storage?.Dispose();
        _storage = null;
    }
}
