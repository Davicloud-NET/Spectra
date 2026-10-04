using System;

namespace SpectraEngine.Core.Graphics.D3D11;

// Last SRV/sampler pair bound to each pixel-shader register, so SetTexture can
// skip a rebind of the same pair.
//
// This mirrors context state, so every site that clears the context's SRV
// slots (BeginPass, ClearState on resize) must call Reset. A stale entry skips
// a bind against a null slot, and D3D11 reads a null SRV as zeros with no error.
internal sealed class D3D11BindCache
{
    // Same range UnbindPixelShaderResources clears. Higher slots are never skipped.
    internal const int TrackedSlots = 8;

    private readonly (nint Srv, nint Sampler)[] _slots = new (nint, nint)[TrackedSlots];

    // Records the pair and returns false only if the slot already holds it.
    public bool MustBind(uint slot, nint srv, nint sampler)
    {
        if (slot >= TrackedSlots)
            return true;

        if (_slots[slot].Srv == srv && _slots[slot].Sampler == sampler)
            return false;

        _slots[slot] = (srv, sampler);
        return true;
    }

    public void Reset() => Array.Clear(_slots);
}
