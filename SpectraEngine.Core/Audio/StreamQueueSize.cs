namespace SpectraEngine.Core.Audio;

// How a streaming voice cuts its sound up for the driver: how many buffers
// are in flight and how many frames each one holds.
internal readonly record struct StreamQueueSize(int BufferCount, int BufferFrames)
{
    public static StreamQueueSize Default =>
        new(StreamingVoice.DefaultBufferCount, StreamingVoice.DefaultBufferFrames);
}
