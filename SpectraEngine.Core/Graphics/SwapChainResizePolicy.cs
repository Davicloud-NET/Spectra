using Silk.NET.Maths;

namespace SpectraEngine.Core.Graphics;

// Shared by both D3D backends: whether to touch the swap chain this frame.
internal static class SwapChainResizePolicy
{
    // requested: the framebuffer-size latch, read once by the caller.
    // lastFailed: a size ResizeBuffers already refused. Not retried until another
    // size has landed; the caller clears it on success.
    // A minimised window reports 0x0, which ResizeBuffers rejects, so skip it
    // and leave the recorded size alone.
    internal static bool ShouldResize(
        Vector2D<int> requested,
        Vector2D<int> current,
        Vector2D<int>? lastFailed,
        bool swapChainAlive,
        bool deviceLost)
    {
        if (requested == current) return false;
        if (!swapChainAlive || deviceLost) return false;
        if (requested.X <= 0 || requested.Y <= 0) return false;
        if (lastFailed == requested) return false;
        return true;
    }
}
