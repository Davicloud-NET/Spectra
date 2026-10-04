using Microsoft.Extensions.Logging;
using Silk.NET.DXGI;

namespace SpectraEngine.Core.Graphics;

// Hands a retired shared target's keyed mutex to the consumer, so a turn still
// queued against it can complete. Shared by both D3D backends. Render thread only.
internal static unsafe class SharedTargetTurn
{
    // WAIT_TIMEOUT is a positive HRESULT, so hr < 0 misses it. Test by value.
    private const int WaitTimeout = 0x00000102;

    // WAIT_ABANDONED: the previous holder went away holding the key.
    private const int WaitAbandoned = 0x00000080;

    // Takes the mutex if it is free and hands it to the consumer.
    // Timeout zero: this runs every frame per retired generation and must not wait.
    internal static void Offer(IDXGIKeyedMutex* mutex, ILogger logger, int generation)
    {
        if (mutex is null) return;

        int hr = mutex->AcquireSync(Renderer.SharedProducerKey, 0u);

        // The consumer already holds the key.
        if (hr == WaitTimeout) return;

        if (hr == WaitAbandoned)
        {
            logger.LogDebug(
                "Retired shared target generation {Generation} had an abandoned key; answering it anyway.",
                generation);
        }
        else if (hr < 0)
        {
            // Debug, not a warning: the next frame offers again.
            logger.LogDebug(
                "Could not take retired shared target generation {Generation}'s key to offer it: 0x{Hr:X8}.",
                generation, hr);
            return;
        }

        hr = mutex->ReleaseSync(Renderer.SharedConsumerKey);
        if (hr < 0)
        {
            logger.LogWarning(
                "Could not hand retired shared target generation {Generation}'s key to the consumer: " +
                "0x{Hr:X8}. A turn queued against it will not complete.",
                generation, hr);
        }
    }
}
