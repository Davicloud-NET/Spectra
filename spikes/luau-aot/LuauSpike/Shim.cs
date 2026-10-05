using System.Runtime.InteropServices;

namespace LuauSpike;

// The entry points of native/spectra_luau.cpp.
internal static unsafe partial class Shim
{
    private const string Lib = "luau";

    // What a host function returns instead of a result count.
    internal const int HostError = -1;
    internal const int HostYieldBase = -2;

    [LibraryImport(Lib)] internal static partial int sl_unwind_mode();
    [LibraryImport(Lib)] internal static partial byte* sl_compiler();
    [LibraryImport(Lib)] internal static partial int sl_abi_value(int which);

    [LibraryImport(Lib)] internal static partial nint sl_newstate();
    [LibraryImport(Lib)] internal static partial void sl_close(nint L);
    [LibraryImport(Lib)] internal static partial void sl_memory(nint L, long* bytes, long* peakBytes, long* allocations);
    [LibraryImport(Lib)] internal static partial void sl_set_memory_limit(nint L, long bytes);
    [LibraryImport(Lib)] internal static partial void sl_free(void* block);

    [LibraryImport(Lib)] internal static partial void sl_pushhostfunction(nint L, delegate* unmanaged[Cdecl]<nint, int> fn, byte* debugname);
    [LibraryImport(Lib)] internal static partial void sl_pushhostnumberfunction(nint L, delegate* unmanaged[Cdecl]<double, double, double> fn, byte* debugname);
    [LibraryImport(Lib)] internal static partial void sl_pushnativeadd(nint L);

    [LibraryImport(Lib)] internal static partial int sl_getfield(nint L, int idx, byte* key);
    [LibraryImport(Lib)] internal static partial int sl_call_numbers(nint L, int functionRef, double a, double b, double* result);

    [LibraryImport(Lib)] internal static partial void sl_interrupt_install(nint L, int installed);
    [LibraryImport(Lib)] internal static partial void sl_stop_arm(nint L);
    [LibraryImport(Lib)] internal static partial void sl_stop_disarm(nint L);
    [LibraryImport(Lib)] internal static partial long sl_interrupt_calls(nint L);

    [LibraryImport(Lib)] internal static partial void sl_track_released_ids(nint L, int tag);
    [LibraryImport(Lib)] internal static partial int sl_drain_released(nint L, int* ids, int capacity);

#if LUAU_COMPILER
    [LibraryImport(Lib)] internal static partial byte* sl_compile(byte* source, nuint size, int optimizationLevel, int debugLevel, int typeInfoLevel, nuint* outsize);
#endif

    internal static long LiveBytes(nint L)
    {
        long bytes, peak, allocations;
        sl_memory(L, &bytes, &peak, &allocations);
        return bytes;
    }
}
