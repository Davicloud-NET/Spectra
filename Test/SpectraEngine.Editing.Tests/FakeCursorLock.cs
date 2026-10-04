using SpectraEngine.Core.Input;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Tests;

// Requests only take effect on Pump(). The real lock is applied by the main
// thread some frames after the render thread asks, and tests need that gap.
internal sealed class FakeCursorLock : ICursorLock
{
    private readonly List<CursorMode> _requests = [];

    // Includes redundant requests; there should be none.
    public IReadOnlyList<CursorMode> Requests => _requests;

    public CursorMode Requested { get; private set; } = CursorMode.Normal;

    public CursorMode CursorMode { get; private set; } = CursorMode.Normal;

    public bool IsCursorLocked => CursorMode == CursorMode.Locked;

    public void RequestCursorMode(CursorMode mode)
    {
        Requested = mode;
        _requests.Add(mode);
    }

    // The main thread's half of the latch.
    public void Pump() => CursorMode = Requested;
}
