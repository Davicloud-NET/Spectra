using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// A renderer-owned buffer of per-instance vertex data, rewritten each frame and
/// consumed by <see cref="Mesh.DrawInstanced"/>. It never grows on its own;
/// the renderer resizes it between frames.
/// </summary>
public abstract class InstanceBuffer : IDisposable
{
    /// <summary>How many instances this buffer can hold.</summary>
    public int Capacity { get; protected set; }

    public int FloatsPerInstance { get; protected set; }

    /// <summary>Instances written since the last <see cref="BeginFrame"/>.</summary>
    public int Cursor { get; protected set; }

    public int Remaining => Capacity - Cursor;

    /// <summary>Starts a frame's writes, discarding what the buffer held. Call once per frame.</summary>
    // Not once per pass: a D3D12 frame is one command list, so a second write
    // at offset 0 changes what draws already recorded will read. Later passes
    // must Append.
    public void BeginFrame()
    {
        Cursor = 0;
        OnBeginFrame();
    }

    /// <summary>Backend hook for <see cref="BeginFrame"/>.</summary>
    protected virtual void OnBeginFrame() { }

    /// <summary>
    /// Appends instances and returns the index of the first, for a draw's
    /// <c>firstInstance</c>. Throws past <see cref="Remaining"/>. Render thread only.
    /// </summary>
    public abstract int Append(ReadOnlySpan<float> data, int instanceCount);

    /// <summary>Replaces the contents: <see cref="BeginFrame"/> then <see cref="Append"/>.</summary>
    public void Update(ReadOnlySpan<float> data, int instanceCount)
    {
        BeginFrame();
        Append(data, instanceCount);
    }

    /// <inheritdoc/>
    public abstract void Dispose();

    /// <summary>
    /// Throws if <paramref name="data"/> is not the right length for
    /// <paramref name="instanceCount"/> instances, or if they do not fit.
    /// </summary>
    protected void ValidateUpdate(ReadOnlySpan<float> data, int instanceCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(instanceCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(instanceCount, Remaining);

        int expected = instanceCount * FloatsPerInstance;
        if (data.Length != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} floats for {instanceCount} instance(s) " +
                $"of {FloatsPerInstance} floats each, got {data.Length}.",
                nameof(data));
        }
    }
}
