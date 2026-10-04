using Silk.NET.Core.Native;
using SpectraEngine.Core.Graphics;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// COM reference counting for <see cref="ComOwnership"/>, against a hand-built
/// COM object so no device is needed.
/// </summary>
public sealed unsafe class ComOwnershipTests
{
    [Fact]
    public void The_ComPtr_constructor_AddRefs_which_is_the_whole_reason_Own_exists()
    {
        using var obj = new FakeComObject();
        obj.RefCount.ShouldBe(1u, "a freshly created COM object starts at one reference");

        var shared = new ComPtr<IUnknown>(obj.Pointer);
        obj.RefCount.ShouldBe(2u, "ComPtr has WRL semantics: it shares a pointer, it does not adopt one");

        shared.Dispose();
        obj.RefCount.ShouldBe(1u, "disposing the ComPtr only drops the reference it added itself");
    }

    [Fact]
    public void Own_leaves_exactly_one_reference_and_disposing_it_destroys_the_object()
    {
        using var obj = new FakeComObject();

        var owned = ComOwnership.Own(obj.Pointer);
        obj.RefCount.ShouldBe(1u, "Own hands the creation reference over rather than adding to it");
        ((nint)owned.Handle).ShouldBe((nint)obj.Pointer, "the raw pointer stays usable through the owning ComPtr");

        owned.Dispose();
        obj.RefCount.ShouldBe(0u, "the last reference goes, so the GPU resource actually dies");
    }

    [Fact]
    public void A_disposed_ComPtr_keeps_its_handle_which_is_why_Release_exists()
    {
        using var obj = new FakeComObject();

        var owned = ComOwnership.Own(obj.Pointer);
        // Own releases once itself, so count from here.
        uint releasesAfterOwn = obj.ReleaseCount;

        owned.Dispose();
        ((nint)owned.Handle).ShouldNotBe((nint)0, "ComPtr.Dispose leaves the handle in place");

        owned.Dispose();
        (obj.ReleaseCount - releasesAfterOwn)
            .ShouldBe(2u, "the second Dispose really did release the object again");
    }

    [Fact]
    public void Release_clears_the_field_so_releasing_twice_is_a_no_op()
    {
        // Both renderers can release twice: resize then shutdown, or a
        // second Shutdown from the crash handler.
        using var obj = new FakeComObject();

        var field = ComOwnership.Own(obj.Pointer);
        uint releasesAfterOwn = obj.ReleaseCount;

        ComOwnership.Release(ref field);
        ComOwnership.Release(ref field);
        field.Dispose();

        (obj.ReleaseCount - releasesAfterOwn)
            .ShouldBe(1u, "exactly one release, no matter how many times the field is let go");
        obj.RefCount.ShouldBe(0u);
        ((nint)field.Handle).ShouldBe((nint)0);
    }

    [Fact]
    public void Releasing_an_empty_field_touches_nothing()
    {
        ComPtr<IUnknown> empty = default;
        ComOwnership.Release(ref empty);
        ((nint)empty.Handle).ShouldBe((nint)0);
    }

    [Fact]
    public void A_null_pointer_round_trips_as_an_empty_ComPtr()
    {
        // QueryInterface probes (info queues) may come back empty.
        var owned = ComOwnership.Own((IUnknown*)null);
        ((nint)owned.Handle).ShouldBe((nint)0);
    }

    // IUnknown vtable plus a counter in native memory, so a real ComPtr can
    // point at it. Starts at one reference, like a Create* result.
    private sealed unsafe class FakeComObject : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Layout
        {
            public void** Vtbl;
            public uint RefCount;
            public uint ReleaseCount;
        }

        private readonly Layout* _object;
        private readonly void** _vtbl;
        private bool _disposed;

        internal FakeComObject()
        {
            _vtbl = (void**)NativeMemory.AllocZeroed(3, (nuint)sizeof(void*));
            _vtbl[0] = (delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)&QueryInterface;
            _vtbl[1] = (delegate* unmanaged[Stdcall]<void*, uint>)&AddRef;
            _vtbl[2] = (delegate* unmanaged[Stdcall]<void*, uint>)&Release;

            _object = (Layout*)NativeMemory.AllocZeroed((nuint)sizeof(Layout));
            _object->Vtbl = _vtbl;
            _object->RefCount = 1;
        }

        internal IUnknown* Pointer => (IUnknown*)_object;

        internal uint RefCount => _object->RefCount;

        // Counted apart from RefCount so an over-release still shows.
        internal uint ReleaseCount => _object->ReleaseCount;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int QueryInterface(void* self, Guid* riid, void** ppv)
        {
            // E_NOINTERFACE
            if (ppv is not null) *ppv = null;
            return unchecked((int)0x80004002);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint AddRef(void* self) => ++((Layout*)self)->RefCount;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint Release(void* self)
        {
            // No free at zero: tests read the counters afterwards.
            ((Layout*)self)->ReleaseCount++;
            ref uint count = ref ((Layout*)self)->RefCount;
            if (count > 0) count--;
            return count;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            NativeMemory.Free(_object);
            NativeMemory.Free(_vtbl);
        }
    }
}
