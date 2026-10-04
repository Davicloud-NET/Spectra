using Silk.NET.Core.Native;

namespace SpectraEngine.Core.Graphics;

// Silk.NET's ComPtr<T> constructor calls AddRef, so new ComPtr<T>(p) on a
// freshly created pointer leaves two references and the object never dies.
// A leaked back buffer reference also makes DXGI refuse ResizeBuffers.
// Wrap every created pointer with Own, and release every field with Release.
internal static unsafe class ComOwnership
{
    // Wraps raw and drops the creation reference, leaving one.
    internal static ComPtr<T> Own<T>(T* raw) where T : unmanaged, IComVtbl<T>
    {
        if (raw is null)
            return default;

        var owned = new ComPtr<T>(raw);
        ((IUnknown*)raw)->Release();
        return owned;
    }

    // Idempotent. ComPtr<T>.Dispose() does not null the handle, and the resize
    // and shutdown paths can both reach the same field.
    internal static void Release<T>(ref ComPtr<T> field) where T : unmanaged, IComVtbl<T>
    {
        if (field.Handle is null)
            return;

        field.Dispose();
        field = default;
    }
}
