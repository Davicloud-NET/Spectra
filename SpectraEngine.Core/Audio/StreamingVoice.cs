using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// A sound played through a queue of small buffers, refilled as the driver
/// finishes them. Used for long sounds and for anything that loops.
/// Render thread only.
/// </summary>
// The voice owns its buffers and takes them off the source before it goes
// back to the pool.
public sealed class StreamingVoice : AudioVoice
{
    /// <summary>Buffers in flight: one playing and three ahead, over half a second of slack.</summary>
    public const int DefaultBufferCount = 4;

    /// <summary>
    /// Frames per buffer, about 170 ms at 48 kHz. Bigger survives longer
    /// stalls but makes a seek land later.
    /// </summary>
    public const int DefaultBufferFrames = 8192;

    // A very short loop gives one run per repetition. Past this the fill is just shorter.
    private const int MaxRunsPerBuffer = 16;

    private readonly IAudioSampleProvider _provider;
    private readonly AudioBufferFormat _bufferFormat;
    private readonly AudioFormat _format;
    private readonly uint[] _buffers;
    private readonly Queue<uint> _idle;
    private readonly short[] _scratch;
    private readonly int _bufferFrames;

    private AudioLoopCursor _cursor;

    internal StreamingVoice(
        IAudioBackend backend,
        uint source,
        IAudioSampleProvider provider,
        AudioSourceSettings settings,
        int bufferCount = DefaultBufferCount,
        int bufferFrames = DefaultBufferFrames)
        : base(backend, source, settings)
    {
        if (bufferCount < 2)
            throw new ArgumentOutOfRangeException(nameof(bufferCount), bufferCount, "A queue needs at least one buffer ahead of the one playing.");
        if (bufferFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferFrames), bufferFrames, "A buffer must hold at least one frame.");

        _provider = provider;
        _format = provider.Format;
        _bufferFormat = _format.Channels == 1 ? AudioBufferFormat.Mono16 : AudioBufferFormat.Stereo16;
        _bufferFrames = bufferFrames;
        _cursor = new AudioLoopCursor(provider.FrameCount, provider.Loop);
        _scratch = new short[(long)bufferFrames * _format.Channels];

        _buffers = new uint[bufferCount];
        _idle = new Queue<uint>(bufferCount);
        for (int i = 0; i < bufferCount; i++)
        {
            _buffers[i] = backend.CreateBuffer();
            _idle.Enqueue(_buffers[i]);
        }

        backend.ConfigureSource(source, in settings);

        // AL refuses to queue on a source still holding a static buffer.
        backend.SetSourceBuffer(source, 0);

        Prime();
    }

    /// <summary>Frames handed to the driver, which is ahead of what it has played.</summary>
    public long PositionFrames => _cursor.Position;

    /// <summary>The region being repeated, or <see cref="LoopRegion.None"/>.</summary>
    public LoopRegion Loop => _cursor.Loop;

    /// <summary>Times the queue ran dry and was restarted. Non-zero means the pump was late.</summary>
    public int UnderrunCount { get; private set; }

    /// <summary>
    /// Jumps to <paramref name="frame"/> and refills from there. A seek past
    /// the loop end plays the tail and finishes.
    /// </summary>
    public void Seek(long frame)
    {
        if (IsFinished) return;

        // AL only unqueues processed buffers, and stopping marks them all processed.
        Backend.Stop(Source);
        DrainQueue();

        _cursor.Seek(frame);
        Prime();
    }

    internal override bool Update()
    {
        if (IsFinished) return false;

        int processed = Backend.GetBuffersProcessed(Source);
        for (int i = 0; i < processed; i++)
        {
            uint buffer = Backend.UnqueueBuffer(Source);
            if (buffer == 0) break;
            _idle.Enqueue(buffer);
        }

        RefillQueue();

        int queued = Backend.GetBuffersQueued(Source);
        if (queued == 0)
        {
            IsFinished = true;
            return false;
        }

        // Don't restart a paused source.
        AudioSourceState state = Backend.GetSourceState(Source);
        if (state is not (AudioSourceState.Playing or AudioSourceState.Paused))
        {
            // Stopped with buffers still queued is an underrun, not the end.
            UnderrunCount++;
            Backend.Play(Source);
        }

        return true;
    }

    internal override void Detach()
    {
        DrainQueue();
        for (int i = 0; i < _buffers.Length; i++)
            Backend.DestroyBuffer(_buffers[i]);

        _idle.Clear();
        base.Detach();
    }

    private void Prime()
    {
        RefillQueue();
        if (Backend.GetBuffersQueued(Source) > 0)
            Backend.Play(Source);
        else
            IsFinished = true;
    }

    private void RefillQueue()
    {
        while (_idle.Count > 0)
        {
            uint buffer = _idle.Peek();
            if (Fill(buffer) <= 0) break;

            _idle.Dequeue();
            Backend.QueueBuffer(Source, buffer);
        }
    }

    // Only legal once the source is stopped.
    private void DrainQueue()
    {
        int queued = Backend.GetBuffersQueued(Source);
        for (int i = 0; i < queued; i++)
        {
            uint buffer = Backend.UnqueueBuffer(Source);
            if (buffer == 0) break;
            _idle.Enqueue(buffer);
        }

        Backend.SetSourceBuffer(Source, 0);
    }

    // Returns frames written.
    private int Fill(uint buffer)
    {
        Span<AudioSegment> runs = stackalloc AudioSegment[MaxRunsPerBuffer];
        int count = _cursor.Plan(runs, _bufferFrames, out long planned);
        if (count == 0 || planned <= 0) return 0;

        int channels = _format.Channels;
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            int wanted = (int)runs[i].Count;
            int read = _provider.ReadFrames(runs[i].Offset, _scratch.AsSpan(written * channels), wanted);
            written += read;

            // Short read: upload what we have. Continuing would put the next
            // run at the wrong scratch offset.
            if (read < wanted) break;
        }

        if (written <= 0) return 0;

        Backend.UploadBuffer(buffer, _bufferFormat, _scratch.AsSpan(0, written * channels), _format.SampleRate);
        return written;
    }
}
