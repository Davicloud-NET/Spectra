using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LuauSpike.Checks;

// Check 5: userdata that refers to a managed object, two ways.
//   handle: the userdata holds a GCHandle and a managed destructor frees it.
//   id:     the userdata holds an index into an ObjectTable. The shim queues
//           collected ids and the host drains the queue.
internal static unsafe class UserdataChecks
{
    private const int HandleTag = 1;
    private const int IdTag = 2;
    private const int ChurnCount = 200_000;

    private static readonly ObjectTable s_objects = new();
    private static int s_liveHandles;
    private static int s_idsCreated;

    internal static void Run(Report report)
    {
        nint L = LuaHost.NewState();
        try
        {
            Lua.lua_setuserdatadtor(L, HandleTag, &ReleaseHandle);
            RegisterCounterType(L, HandleTag, &HandleAdd, &HandleGet);
            Shim.sl_track_released_ids(L, IdTag);
            RegisterCounterType(L, IdTag, &IdAdd, &IdGet);

            int module = Scripts.LoadModule(L, "userdata");
            report.Run("5a", "userdata lifetime, GCHandle", () => Lifetime(report, "5a", "userdata lifetime, GCHandle", L, module, useHandles: true));
            report.Run("5b", "userdata lifetime, id table", () => Lifetime(report, "5b", "userdata lifetime, id table", L, module, useHandles: false));
            report.Run("5c", "userdata churn", () => Churn(report, L, module));
        }
        finally
        {
            Shim.sl_close(L);
        }
    }

    private static void Lifetime(Report report, string id, string what, nint L, int module, bool useHandles)
    {
        WeakReference weak = HoldNewCounter(L, module, useHandles);
        CollectManaged();
        bool aliveWhileHeld = weak.IsAlive;

        double first = Use(L, module, 5);
        double second = Use(L, module, 7);

        LuaStack.PushMember(L, module, "drop"u8);
        LuaStack.Call(L, 0, 0);
        Lua.lua_gc(L, Lua.GcCollect, 0);
        int stillTracked = useHandles ? s_liveHandles : DrainReleased(L);
        CollectManaged();
        bool collected = !weak.IsAlive;

        bool passed = aliveWhileHeld && first == 5 && second == 12 && stillTracked == 0 && collected;
        report.Check(id, what, passed,
            $"alive after a .NET GC while Luau held it: {aliveWhileHeld}; methods returned {first} and {second}; released by the Luau GC: {stillTracked == 0}; collected by .NET afterwards: {collected}");
    }

    // Not inlined, so no reference to the Counter stays in the caller's frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference HoldNewCounter(nint L, int module, bool useHandles)
    {
        Counter counter = new();
        LuaStack.PushMember(L, module, "hold"u8);
        if (useHandles)
            PushHandle(L, counter);
        else
            PushId(L, counter);
        LuaStack.Call(L, 1, 0);
        return new WeakReference(counter);
    }

    private static double Use(nint L, int module, double amount)
    {
        LuaStack.PushMember(L, module, "use"u8);
        Lua.lua_pushnumber(L, amount);
        LuaStack.Call(L, 1, 1);
        double value = Lua.ToNumber(L, -1);
        Lua.Pop(L, 1);
        return value;
    }

    private static void Churn(Report report, nint L, int module)
    {
        double handleNanoseconds = ChurnOnce(L, module, useHandles: true);
        bool handlesBalanced = s_liveHandles == 0;
        double idNanoseconds = ChurnOnce(L, module, useHandles: false);
        bool idsBalanced = s_objects.Count == 0;

        report.Check("5c", "userdata churn", handlesBalanced && idsBalanced,
            $"{ChurnCount:N0} objects created, used once and collected; every one released in both designs");
        report.Note("5c", "create, one call, collect", $"GCHandle {handleNanoseconds:F0} ns/object, id table {idNanoseconds:F0} ns/object");
    }

    private static double ChurnOnce(nint L, int module, bool useHandles)
    {
        long start = Stopwatch.GetTimestamp();
        LuaStack.PushMember(L, module, "churn"u8);
        fixed (byte* name = "create"u8)
            Shim.sl_pushhostfunction(L, useHandles ? &CreateHandle : &CreateId, name);
        Lua.lua_pushnumber(L, ChurnCount);
        LuaStack.Call(L, 2, 0);
        Lua.lua_gc(L, Lua.GcCollect, 0);
        if (!useHandles)
            DrainReleased(L);
        return Bench.Nanoseconds(Stopwatch.GetTimestamp() - start) / ChurnCount;
    }

    private static void CollectManaged()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void RegisterCounterType(nint L, int tag, delegate* unmanaged[Cdecl]<nint, int> add, delegate* unmanaged[Cdecl]<nint, int> get)
    {
        Lua.lua_createtable(L, 0, 1);
        Lua.lua_createtable(L, 0, 2);
        fixed (byte* name = "Counter.add"u8)
            Shim.sl_pushhostfunction(L, add, name);
        LuaStack.SetField(L, -2, "add"u8);
        fixed (byte* name = "Counter.get"u8)
            Shim.sl_pushhostfunction(L, get, name);
        LuaStack.SetField(L, -2, "get"u8);
        LuaStack.SetField(L, -2, "__index"u8);
        Lua.lua_setuserdatametatable(L, tag);
    }

    private static void PushHandle(nint L, Counter counter)
    {
        void* block = Lua.lua_newuserdatataggedwithmetatable(L, (nuint)sizeof(nint), HandleTag);
        *(nint*)block = GCHandle.ToIntPtr(GCHandle.Alloc(counter));
        s_liveHandles++;
    }

    private static void PushId(nint L, Counter counter)
    {
        // Stands in for the once-a-frame drain a real host would do.
        if ((++s_idsCreated & 1023) == 0)
            DrainReleased(L);

        void* block = Lua.lua_newuserdatataggedwithmetatable(L, sizeof(int), IdTag);
        *(int*)block = s_objects.Add(counter);
    }

    private static Counter? HandleTarget(nint L)
    {
        void* block = Lua.lua_touserdatatagged(L, 1, HandleTag);
        return block == null ? null : GCHandle.FromIntPtr(*(nint*)block).Target as Counter;
    }

    private static Counter? IdTarget(nint L)
    {
        void* block = Lua.lua_touserdatatagged(L, 1, IdTag);
        return block == null ? null : s_objects.Get(*(int*)block) as Counter;
    }

    // Returns how many objects the table still holds.
    private static int DrainReleased(nint L)
    {
        int* ids = stackalloc int[256];
        int count;
        while ((count = Shim.sl_drain_released(L, ids, 256)) > 0)
        {
            for (int i = 0; i < count; i++)
                s_objects.Release(ids[i]);
        }

        return s_objects.Count;
    }

    // Runs inside the Luau collector. No Luau calls from here.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReleaseHandle(nint L, void* userdata)
    {
        GCHandle.FromIntPtr(*(nint*)userdata).Free();
        s_liveHandles--;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CreateHandle(nint L)
    {
        try
        {
            PushHandle(L, new Counter());
            return 1;
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return HostFunctions.Fail(L, e.Message);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CreateId(nint L)
    {
        try
        {
            PushId(L, new Counter());
            return 1;
        }
        catch (Exception e) // boundary: a managed exception must not reach a native frame
        {
            return HostFunctions.Fail(L, e.Message);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int HandleAdd(nint L) => Add(L, HandleTarget(L));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int HandleGet(nint L) => Get(L, HandleTarget(L));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int IdAdd(nint L) => Add(L, IdTarget(L));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int IdGet(nint L) => Get(L, IdTarget(L));

    private static int Add(nint L, Counter? counter)
    {
        if (counter is null)
            return HostFunctions.Fail(L, "Counter expected");

        counter.Value += (int)Lua.ToNumber(L, 2);
        return 0;
    }

    private static int Get(nint L, Counter? counter)
    {
        if (counter is null)
            return HostFunctions.Fail(L, "Counter expected");

        Lua.lua_pushnumber(L, counter.Value);
        return 1;
    }
}
