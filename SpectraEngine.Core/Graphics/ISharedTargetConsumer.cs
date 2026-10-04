using System;

namespace SpectraEngine.Core.Graphics;

// Stand-in for a compositor on the other end of the shared target's keyed
// mutex, used by the pacing probe.
// Must run on a second device: a keyed mutex is owned per device, and one that
// already holds it gets DXGI_ERROR_INVALID_CALL instead of waiting.
internal interface ISharedTargetConsumer : IDisposable
{
    // Acquire the consumer key, snapshot, release the producer key. False on
    // timeout (producer still in its frame, not an error) or a failed acquire.
    bool TakeTurn(int timeoutMs);
}
