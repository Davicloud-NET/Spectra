// The native layer between managed code and the Luau VM.
//
// Luau raises errors by longjmp or by a C++ throw. Either one unwinds the stack
// up to the nearest protected call. Every raise that managed code can cause is
// made here, after the managed frame has returned, so the unwind only ever
// crosses native frames.

#include "lua.h"
#include "lualib.h"

#include <atomic>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifndef SL_API
#define SL_API extern "C"
#endif

namespace
{

struct HostState
{
    int64_t bytes;
    int64_t peakBytes;
    int64_t allocations;
    int64_t byteLimit; // 0 means no limit
    int64_t interruptCalls;
    std::atomic<int> stopRequested;

    // Ids of collected userdata, waiting for the host to drain them.
    int32_t* released;
    int releasedCount;
    int releasedCapacity;
};

HostState* hostOf(lua_State* L)
{
    return static_cast<HostState*>(lua_callbacks(L)->userdata);
}

void* countingAlloc(void* ud, void* ptr, size_t osize, size_t nsize)
{
    HostState* host = static_cast<HostState*>(ud);
    int64_t after = host->bytes + static_cast<int64_t>(nsize) - static_cast<int64_t>(osize);

    // Refusing makes Luau raise "not enough memory" from whichever call allocated.
    if (host->byteLimit > 0 && nsize > osize && after > host->byteLimit)
        return nullptr;

    host->bytes = after;
    if (host->bytes > host->peakBytes)
        host->peakBytes = host->bytes;

    if (nsize == 0)
    {
        free(ptr);
        return nullptr;
    }

    host->allocations++;
    return realloc(ptr, nsize);
}

// Only reached in a longjmp build, when an error is raised with no protected call active.
void panicHandler(lua_State* L, int errcode)
{
    const char* message = lua_gettop(L) > 0 ? lua_tostring(L, -1) : nullptr;
    fprintf(stderr, "luau panic: unprotected error %d: %s\n", errcode, message ? message : "(no message)");
    fflush(stderr);
}

void interruptHandler(lua_State* L, int gc)
{
    // gc >= 0 means the collector is running. Raising is not allowed there.
    if (gc >= 0)
        return;

    HostState* host = hostOf(L);
    host->interruptCalls++;
    if (host->stopRequested.load(std::memory_order_relaxed) == 0)
        return;

    lua_rawcheckstack(L, 1);
    luaL_error(L, "script stopped by host");
}

void queueReleasedId(lua_State* L, void* userdata)
{
    HostState* host = hostOf(L);
    if (host->releasedCount == host->releasedCapacity)
    {
        int capacity = host->releasedCapacity == 0 ? 256 : host->releasedCapacity * 2;
        void* grown = realloc(host->released, sizeof(int32_t) * static_cast<size_t>(capacity));
        if (!grown)
            return;
        host->released = static_cast<int32_t*>(grown);
        host->releasedCapacity = capacity;
    }

    int32_t id;
    memcpy(&id, userdata, sizeof(id));
    host->released[host->releasedCount++] = id;
}

typedef int (*HostFunction)(lua_State* L);
typedef double (*HostNumberFunction)(double a, double b);

const int HostError = -1;
const int HostYieldBase = -2;

// The only lua_CFunction Luau ever sees for a managed function.
int callHost(lua_State* L)
{
    HostFunction fn = reinterpret_cast<HostFunction>(lua_tolightuserdata(L, lua_upvalueindex(1)));
    int result = fn(L);
    if (result >= 0)
        return result;

    // The managed frame is gone. Raising from here crosses native frames only.
    if (result == HostError)
    {
        if (lua_type(L, -1) == LUA_TSTRING)
        {
            // Prefix the script position, as luaL_error does.
            luaL_where(L, 1);
            lua_insert(L, -2);
            lua_concat(L, 2);
        }
        lua_error(L);
    }

    return lua_yield(L, HostYieldBase - result);
}

// Arguments are checked and unpacked here, so the managed function makes no call back into Luau.
int callHostNumbers(lua_State* L)
{
    HostNumberFunction fn = reinterpret_cast<HostNumberFunction>(lua_tolightuserdata(L, lua_upvalueindex(1)));
    double a = luaL_checknumber(L, 1);
    double b = luaL_checknumber(L, 2);
    lua_pushnumber(L, fn(a, b));
    return 1;
}

int nativeAdd(lua_State* L)
{
    lua_pushnumber(L, lua_tonumber(L, 1) + lua_tonumber(L, 2));
    return 1;
}

int getFieldBody(lua_State* L)
{
    lua_getfield(L, 1, static_cast<const char*>(lua_tolightuserdata(L, 2)));
    return 1;
}

} // namespace

SL_API int sl_unwind_mode()
{
    return LUA_USE_LONGJMP;
}

SL_API const char* sl_compiler()
{
#if defined(__clang__)
    return "clang " __clang_version__;
#elif defined(_MSC_VER)
#define SL_STR2(x) #x
#define SL_STR(x) SL_STR2(x)
    return "msvc " SL_STR(_MSC_FULL_VER);
#else
    return "unknown";
#endif
}

// Values the managed binding hardcodes. They come from Luau's headers and move
// between releases, so the host compares them at startup.
SL_API int sl_abi_value(int which)
{
    switch (which)
    {
    case 0:
        return LUA_REGISTRYINDEX;
    case 1:
        return LUA_GLOBALSINDEX;
    case 2:
        return LUA_TNUMBER;
    case 3:
        return LUA_TSTRING;
    case 4:
        return LUA_TTABLE;
    case 5:
        return LUA_TFUNCTION;
    case 6:
        return LUA_TUSERDATA;
    case 7:
        return LUA_UTAG_LIMIT;
    default:
        return -1;
    }
}

SL_API lua_State* sl_newstate()
{
    HostState* host = static_cast<HostState*>(calloc(1, sizeof(HostState)));
    if (!host)
        return nullptr;

    lua_State* L = lua_newstate(countingAlloc, host);
    if (!L)
    {
        free(host);
        return nullptr;
    }

    lua_Callbacks* callbacks = lua_callbacks(L);
    callbacks->userdata = host;
    callbacks->panic = panicHandler;
    return L;
}

SL_API void sl_close(lua_State* L)
{
    HostState* host = hostOf(L);
    lua_close(L);
    free(host->released);
    free(host);
}

SL_API void sl_memory(lua_State* L, int64_t* bytes, int64_t* peakBytes, int64_t* allocations)
{
    HostState* host = hostOf(L);
    *bytes = host->bytes;
    *peakBytes = host->peakBytes;
    *allocations = host->allocations;
}

SL_API void sl_set_memory_limit(lua_State* L, int64_t bytes)
{
    hostOf(L)->byteLimit = bytes;
}

SL_API void sl_free(void* block)
{
    free(block);
}

SL_API void sl_pushhostfunction(lua_State* L, HostFunction fn, const char* debugname)
{
    lua_pushlightuserdata(L, reinterpret_cast<void*>(fn));
    lua_pushcclosure(L, callHost, debugname, 1);
}

SL_API void sl_pushhostnumberfunction(lua_State* L, HostNumberFunction fn, const char* debugname)
{
    lua_pushlightuserdata(L, reinterpret_cast<void*>(fn));
    lua_pushcclosure(L, callHostNumbers, debugname, 1);
}

SL_API void sl_pushnativeadd(lua_State* L)
{
    lua_pushcfunction(L, nativeAdd, "native_add");
}

// lua_getfield can run an __index metamethod, so it can raise. This form
// returns a status and leaves the value or the error message on the stack.
SL_API int sl_getfield(lua_State* L, int idx, const char* key)
{
    idx = lua_absindex(L, idx);
    lua_pushcfunction(L, getFieldBody, "sl_getfield");
    lua_pushvalue(L, idx);
    lua_pushlightuserdata(L, const_cast<char*>(key));
    return lua_pcall(L, 2, 1, 0);
}

// A whole call in one transition: f(a, b) -> number.
SL_API int sl_call_numbers(lua_State* L, int functionRef, double a, double b, double* result)
{
    lua_rawgeti(L, LUA_REGISTRYINDEX, functionRef);
    lua_pushnumber(L, a);
    lua_pushnumber(L, b);
    int status = lua_pcall(L, 2, 1, 0);
    if (status == LUA_OK)
    {
        *result = lua_tonumber(L, -1);
        lua_pop(L, 1);
    }
    return status;
}

// Installed all the time: one native call per loop back edge and per call.
SL_API void sl_interrupt_install(lua_State* L, int installed)
{
    lua_callbacks(L)->interrupt = installed ? interruptHandler : nullptr;
}

// Safe from any thread. The handler stays armed until the host disarms it, so
// a script that catches the error with pcall is stopped again at its next loop.
SL_API void sl_stop_arm(lua_State* L)
{
    hostOf(L)->stopRequested.store(1, std::memory_order_relaxed);
    lua_callbacks(L)->interrupt = interruptHandler;
}

SL_API void sl_stop_disarm(lua_State* L)
{
    lua_callbacks(L)->interrupt = nullptr;
    hostOf(L)->stopRequested.store(0, std::memory_order_relaxed);
}

SL_API int64_t sl_interrupt_calls(lua_State* L)
{
    return hostOf(L)->interruptCalls;
}

// Userdata with this tag holds a 4-byte id. Collection queues the id here and
// the host drains the queue later, so the collector never calls managed code.
SL_API void sl_track_released_ids(lua_State* L, int tag)
{
    lua_setuserdatadtor(L, tag, queueReleasedId);
}

SL_API int sl_drain_released(lua_State* L, int32_t* ids, int capacity)
{
    HostState* host = hostOf(L);
    int count = host->releasedCount < capacity ? host->releasedCount : capacity;
    memcpy(ids, host->released + (host->releasedCount - count), sizeof(int32_t) * static_cast<size_t>(count));
    host->releasedCount -= count;
    return count;
}
