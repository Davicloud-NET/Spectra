using SpectraEngine.Core.Audio;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

// IAudioBackend with no device (CI has no sound card). Models only the AL
// source states and buffer queues the engine reads back.
internal sealed class FakeAudioBackend : IAudioBackend
{
    private sealed class SourceRecord
    {
        public AudioSourceState State = AudioSourceState.Initial;
        public uint StaticBuffer;
        public readonly List<uint> Queue = [];

        // Buffers at the head of the queue the driver has finished with.
        public int Processed;
    }

    private readonly Dictionary<uint, SourceRecord> _sources = [];
    private readonly Dictionary<uint, short[]> _buffers = [];
    private uint _nextSource = 1;
    private uint _nextBuffer = 1;

    public FakeAudioBackend(int maxSources = 32) => MaxSources = maxSources;

    public int MaxSources { get; }

    public string DeviceName => "fake";

    public bool IsDisposed { get; private set; }

    // Non-zero at the end of a test is a leak.
    public int LiveBufferCount => _buffers.Count;

    public int LiveSourceCount => _sources.Count;

    // Every buffer upload since the last reset, oldest first.
    public List<short[]> Uploads { get; } = [];

    // Pretends the driver consumed queued buffers.
    public void Consume(uint source, int count)
    {
        SourceRecord record = _sources[source];
        record.Processed = Math.Min(record.Queue.Count, record.Processed + count);
    }

    // An underrun: everything queued played out and the source stopped.
    public void Starve(uint source)
    {
        SourceRecord record = _sources[source];
        record.Processed = record.Queue.Count;
        record.State = AudioSourceState.Stopped;
    }

    // A one-shot finishing on its own.
    public void Finish(uint source) => _sources[source].State = AudioSourceState.Stopped;

    public AudioSourceState StateOf(uint source) => _sources[source].State;

    public int QueueDepth(uint source) => _sources[source].Queue.Count;

    public short[] Contents(uint buffer) => _buffers[buffer];

    public uint CreateBuffer()
    {
        uint buffer = _nextBuffer++;
        _buffers[buffer] = [];
        return buffer;
    }

    public void DestroyBuffer(uint buffer) => _buffers.Remove(buffer);

    public void UploadBuffer(uint buffer, AudioBufferFormat format, ReadOnlySpan<short> pcm, int sampleRate)
    {
        short[] copy = pcm.ToArray();
        _buffers[buffer] = copy;
        Uploads.Add(copy);
    }

    public bool TryCreateSource(out uint source)
    {
        if (_sources.Count >= MaxSources)
        {
            source = 0;
            return false;
        }

        source = _nextSource++;
        _sources[source] = new SourceRecord();
        return true;
    }

    public void DestroySource(uint source) => _sources.Remove(source);

    public void ConfigureSource(uint source, in AudioSourceSettings settings)
    {
        _ = _sources[source];
        _ = settings;
    }

    public AudioSourceState GetSourceState(uint source) => _sources[source].State;

    public int GetBuffersProcessed(uint source) => _sources[source].Processed;

    public int GetBuffersQueued(uint source) => _sources[source].Queue.Count;

    public void SetSourceBuffer(uint source, uint buffer) => _sources[source].StaticBuffer = buffer;

    // Like real AL, queueing leaves the source state alone.
    public void QueueBuffer(uint source, uint buffer) => _sources[source].Queue.Add(buffer);

    public uint UnqueueBuffer(uint source)
    {
        SourceRecord record = _sources[source];
        if (record.Processed <= 0 || record.Queue.Count == 0)
            return 0;

        uint buffer = record.Queue[0];
        record.Queue.RemoveAt(0);
        record.Processed--;
        return buffer;
    }

    public void Play(uint source) => _sources[source].State = AudioSourceState.Playing;

    public void Stop(uint source)
    {
        SourceRecord record = _sources[source];
        record.State = AudioSourceState.Stopped;

        // AL marks every queued buffer processed on a stop, so the queue can be drained.
        record.Processed = record.Queue.Count;
    }

    public void Pause(uint source) => _sources[source].State = AudioSourceState.Paused;

    public void SetListener(Vector3 position, Vector3 velocity, Vector3 forward, Vector3 up)
    {
        ListenerPosition = position;
        ListenerVelocity = velocity;
        ListenerForward = forward;
        ListenerUp = up;
    }

    public void SetListenerGain(float gain) => ListenerGain = gain;

    public Vector3 ListenerPosition { get; private set; }

    public Vector3 ListenerVelocity { get; private set; }

    public Vector3 ListenerForward { get; private set; }

    public Vector3 ListenerUp { get; private set; }

    public float ListenerGain { get; private set; } = 1f;

    public void Dispose() => IsDisposed = true;
}
