// The native layer between managed code and the Luau VM. A Luau error is a
// longjmp, and one that crosses a managed frame kills a NativeAOT process, so
// managed code reaches Luau through this file only. native/README.md has the
// rule and what each part here does for it.

#include "spectra_luau.h"

#include "lua.h"
#include "lualib.h"

#include "Luau/Common.h"
#include "Luau/ExperimentalFlags.h"

#include <atomic>
#include <new>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#if !LUA_USE_LONGJMP
#error "Build Luau with LUA_USE_LONGJMP=1. The binding checks for it at startup."
#endif

#if LUA_VECTOR_SIZE != 3 || LUA_VECTOR_DOUBLE != 0
#error "The binding expects vectors of three floats."
#endif

// What a panic or a failed allocation left, for a debugger or a crash dump.
// Nothing is printed: a game has no console. Not static, so the writes stay.
char sl_fatal_message[512];

namespace
{

enum Body
{
    OpenLibsBody,
    LoadBody,
    PushHostFunctionBody,
    PushStringBody,
    CreateTableBody,
    NewUserdataBody,
    GetFieldBody,
    SetFieldBody,
    ToStringBody,
    BodyCount,
};

struct HostState
{
    // Written by the thread that runs the state, read by any.
    std::atomic<int64_t> bytes{0};

    // Written by any thread, read by the one that runs the state.
    std::atomic<int> stopRequested{0};

    // Registry references to the functions the status forms call.
    int bodies[BodyCount] = {};
};

typedef int (*HostFunction)(lua_State* L);

struct Chunk
{
    const char* name;
    const char* bytecode;
    size_t size;
};

struct HostClosure
{
    HostFunction function;
    const char* debugName;
};

struct Text
{
    const char* data;
    size_t length;
};

struct TableSize
{
    int array;
    int record;
};

struct UserdataRequest
{
    size_t size;
    int tag;
    void* block;
};

void keepFatalMessage(const char* prefix, const char* message)
{
    size_t used = strlen(prefix);
    if (used >= sizeof(sl_fatal_message))
        used = sizeof(sl_fatal_message) - 1;
    memcpy(sl_fatal_message, prefix, used);

    size_t length = message ? strlen(message) : 0;
    if (length > sizeof(sl_fatal_message) - 1 - used)
        length = sizeof(sl_fatal_message) - 1 - used;
    if (length > 0)
        memcpy(sl_fatal_message + used, message, length);
    sl_fatal_message[used + length] = 0;
}

HostState* hostOf(lua_State* L)
{
    return static_cast<HostState*>(lua_callbacks(L)->userdata);
}

void* countingAlloc(void* ud, void* ptr, size_t osize, size_t nsize)
{
    HostState* host = static_cast<HostState*>(ud);
    int64_t bytes = host->bytes.load(std::memory_order_relaxed);

    if (nsize == 0)
    {
        free(ptr);
        host->bytes.store(bytes - static_cast<int64_t>(osize), std::memory_order_relaxed);
        return nullptr;
    }

    void* block = realloc(ptr, nsize);
    if (!block)
    {
        // Null would make Luau raise from whichever call allocated, and that
        // call can have a managed frame under it. So there is no limit here,
        // and a system with no memory left ends the process.
        keepFatalMessage("luau: out of memory", nullptr);
        abort();
    }

    host->bytes.store(bytes + static_cast<int64_t>(nsize) - static_cast<int64_t>(osize), std::memory_order_relaxed);
    return block;
}

// Reached when an error is raised with no protected call active. Luau calls
// abort() when this returns.
void panicHandler(lua_State* L, int errcode)
{
    const char* message = lua_gettop(L) > 0 && lua_type(L, -1) == LUA_TSTRING ? lua_tostring(L, -1) : "(no message)";
    keepFatalMessage("luau: unprotected error: ", message);
}

// Pushes "chunk:line: " for the innermost script function, or an empty string.
void pushPosition(lua_State* L)
{
    lua_Debug ar;
    for (int level = 0; lua_getinfo(L, level, "sl", &ar); level++)
    {
        if (ar.currentline > 0)
        {
            lua_pushfstring(L, "%s:%d: ", ar.short_src, ar.currentline);
            return;
        }
    }

    lua_pushliteral(L, "");
}

// Installed for the life of the state. Arming only sets the flag, so no other
// thread ever writes into the VM's own data.
void interruptHandler(lua_State* L, int gc)
{
    // gc >= 0 means the collector is running. Raising is not allowed there.
    if (gc >= 0)
        return;

    if (hostOf(L)->stopRequested.load(std::memory_order_relaxed) == 0)
        return;

    lua_rawcheckstack(L, 2);
    pushPosition(L);
    lua_pushliteral(L, "script stopped by the host");
    lua_concat(L, 2);
    lua_error(L);
}

// The only lua_CFunction Luau sees for a managed function.
int callHost(lua_State* L)
{
    HostFunction function = reinterpret_cast<HostFunction>(lua_tolightuserdata(L, lua_upvalueindex(1)));
    int result = function(L);

    // The managed frame is gone. A raise from here crosses native frames only.
    int top = lua_gettop(L);
    if (result >= 0)
    {
        if (result > top)
            luaL_error(L, "a host function returned more values than it left on the stack");
        return result;
    }

    if (result == SL_HOST_ERROR || result == SL_HOST_RAISE)
    {
        bool wantsPosition = result == SL_HOST_ERROR;
        if (top == 0)
        {
            lua_pushliteral(L, "a host function failed without a message");
            wantsPosition = true;
        }
        if (wantsPosition && lua_type(L, -1) == LUA_TSTRING)
        {
            pushPosition(L);
            lua_insert(L, -2);
            lua_concat(L, 2);
        }
        lua_error(L);
    }

    int yielded = SL_HOST_YIELD - result;
    if (yielded > top)
        luaL_error(L, "a host function yielded more values than it left on the stack");
    return lua_yield(L, yielded);
}

int openLibsBody(lua_State* L)
{
    luaL_openlibs(L);
    return 0;
}

int loadBody(lua_State* L)
{
    const Chunk* chunk = static_cast<const Chunk*>(lua_tolightuserdata(L, 1));
    if (luau_load(L, chunk->name, chunk->bytecode, chunk->size, 0) != 0)
        lua_error(L);
    return 1;
}

int pushHostFunctionBody(lua_State* L)
{
    const HostClosure* closure = static_cast<const HostClosure*>(lua_tolightuserdata(L, 1));
    lua_pushlightuserdata(L, reinterpret_cast<void*>(closure->function));
    lua_pushcclosure(L, callHost, closure->debugName, 1);
    return 1;
}

int pushStringBody(lua_State* L)
{
    const Text* text = static_cast<const Text*>(lua_tolightuserdata(L, 1));
    lua_pushlstring(L, text->data ? text->data : "", text->length);
    return 1;
}

int createTableBody(lua_State* L)
{
    const TableSize* size = static_cast<const TableSize*>(lua_tolightuserdata(L, 1));
    if (size->array < 0 || size->record < 0)
        luaL_error(L, "a table size is negative");
    lua_createtable(L, size->array, size->record);
    return 1;
}

int newUserdataBody(lua_State* L)
{
    UserdataRequest* request = static_cast<UserdataRequest*>(lua_tolightuserdata(L, 1));
    if (static_cast<unsigned>(request->tag) >= LUA_UTAG_LIMIT)
        luaL_error(L, "userdata tag %d is out of range", request->tag);

    request->block = lua_newuserdatatagged(L, request->size, request->tag);
    return 1;
}

// Arguments: the key, then the object.
int getFieldBody(lua_State* L)
{
    lua_getfield(L, 2, static_cast<const char*>(lua_tolightuserdata(L, 1)));
    return 1;
}

// Arguments: the key, the object, then the value.
int setFieldBody(lua_State* L)
{
    lua_setfield(L, 2, static_cast<const char*>(lua_tolightuserdata(L, 1)));
    return 0;
}

int toStringBody(lua_State* L)
{
    luaL_tolstring(L, 1, nullptr);
    return 1;
}

struct BodyEntry
{
    lua_CFunction function;
    const char* name;
};

// In the order of Body.
const BodyEntry bodyEntries[BodyCount] = {
    {openLibsBody, "sl_openlibs"},
    {loadBody, "sl_load"},
    {pushHostFunctionBody, "sl_pushhostfunction"},
    {pushStringBody, "sl_pushstring"},
    {createTableBody, "sl_createtable"},
    {newUserdataBody, "sl_newuserdata"},
    {getFieldBody, "sl_getfield"},
    {setFieldBody, "sl_setfield"},
    {toStringBody, "sl_tostring"},
};

const char stackOverflow[] = "stack overflow";

// Leaves the message where the result would be. The values the call would
// have taken go first. Room is made past the frame's limit, which a push
// would raise on, so no other value has to go.
int refuse(lua_State* L, int consumed, const char* message)
{
    lua_pop(L, consumed);
    lua_rawcheckstack(L, 1);
    lua_pushstring(L, message);
    return LUA_ERRRUN;
}

// Why this thread cannot make a protected call that needs so many more slots,
// or null. Luau asks about the thread's state in a debug build only. A call on
// a thread that has yielded moves its stack base under the host.
const char* whyNot(lua_State* L, int slots)
{
    int status = lua_status(L);
    if (status == LUA_YIELD || status == LUA_BREAK)
        return "the thread is suspended";
    if (status != LUA_OK)
        return "the thread ended with an error";

    // A push raises once one frame holds LUAI_MAXCSTACK values. This answers.
    return slots > 0 && !lua_checkstack(L, slots) ? stackOverflow : nullptr;
}

void pushBody(lua_State* L, Body body)
{
    lua_getref(L, hostOf(L)->bodies[body]);
}

// Calls a body with one argument. Leaves its results or the error value on top.
int protect(lua_State* L, Body body, void* argument, int results)
{
    if (const char* reason = whyNot(L, 2))
        return refuse(L, 0, reason);

    pushBody(L, body);
    lua_pushlightuserdata(L, argument);
    return lua_pcall(L, 1, results, 0);
}

struct Constant
{
    const char* name;
    int value;
};

#define SL_CONSTANT(name) {#name, name}

const Constant constants[] = {
    SL_CONSTANT(SL_LUAU_RELEASE),
    SL_CONSTANT(SL_SHIM_REVISION),
    SL_CONSTANT(SL_HOST_ERROR),
    SL_CONSTANT(SL_HOST_RAISE),
    SL_CONSTANT(SL_HOST_YIELD),

    SL_CONSTANT(LUA_USE_LONGJMP),
    SL_CONSTANT(LUA_VECTOR_SIZE),
    SL_CONSTANT(LUA_VECTOR_DOUBLE),
    SL_CONSTANT(LUA_MINSTACK),
    SL_CONSTANT(LUAI_MAXCSTACK),
    SL_CONSTANT(LUA_UTAG_LIMIT),
    SL_CONSTANT(LUA_LUTAG_LIMIT),

    SL_CONSTANT(LUA_MULTRET),
    SL_CONSTANT(LUA_REGISTRYINDEX),
    SL_CONSTANT(LUA_ENVIRONINDEX),
    SL_CONSTANT(LUA_GLOBALSINDEX),
    SL_CONSTANT(LUA_NOREF),
    SL_CONSTANT(LUA_REFNIL),

    SL_CONSTANT(LUA_OK),
    SL_CONSTANT(LUA_YIELD),
    SL_CONSTANT(LUA_ERRRUN),
    SL_CONSTANT(LUA_ERRSYNTAX),
    SL_CONSTANT(LUA_ERRMEM),
    SL_CONSTANT(LUA_ERRERR),
    SL_CONSTANT(LUA_BREAK),

    SL_CONSTANT(LUA_TNONE),
    SL_CONSTANT(LUA_TNIL),
    SL_CONSTANT(LUA_TBOOLEAN),
    SL_CONSTANT(LUA_TLIGHTUSERDATA),
    SL_CONSTANT(LUA_TNUMBER),
    SL_CONSTANT(LUA_TINTEGER),
    SL_CONSTANT(LUA_TVECTOR),
    SL_CONSTANT(LUA_TSTRING),
    SL_CONSTANT(LUA_TTABLE),
    SL_CONSTANT(LUA_TFUNCTION),
    SL_CONSTANT(LUA_TUSERDATA),
    SL_CONSTANT(LUA_TTHREAD),
    SL_CONSTANT(LUA_TBUFFER),
    SL_CONSTANT(LUA_TCLASS),
    SL_CONSTANT(LUA_TOBJECT),

    SL_CONSTANT(LUA_GCSTOP),
    SL_CONSTANT(LUA_GCRESTART),
    SL_CONSTANT(LUA_GCCOLLECT),
    SL_CONSTANT(LUA_GCCOUNT),
    SL_CONSTANT(LUA_GCCOUNTB),
    SL_CONSTANT(LUA_GCISRUNNING),
    SL_CONSTANT(LUA_GCSTEP),
    SL_CONSTANT(LUA_GCSETGOAL),
    SL_CONSTANT(LUA_GCSETSTEPMUL),
    SL_CONSTANT(LUA_GCSETSTEPSIZE),
};

} // namespace

// Luau's own tools turn every feature flag on that is not experimental. A
// build that sets none runs another dialect than the one luau-lsp checks.
void sl_apply_flags()
{
    static const bool applied = [] {
        for (Luau::FValue<bool>* flag = Luau::FValue<bool>::list; flag; flag = flag->next)
            if (strncmp(flag->name, "Luau", 4) == 0 && !Luau::isAnalysisFlagExperimental(flag->name))
                flag->value = true;
        return true;
    }();
    (void)applied;
}

// A value the binding copied from Luau's headers or from this file. Returns 0
// for a name this build does not know.
SL_API int sl_constant(const char* name, int* value)
{
    for (const Constant& constant : constants)
    {
        if (strcmp(constant.name, name) == 0)
        {
            *value = constant.value;
            return 1;
        }
    }
    return 0;
}

SL_API lua_State* sl_newstate()
{
    sl_apply_flags();

    HostState* host = new (std::nothrow) HostState();
    if (!host)
        return nullptr;

    lua_State* L = lua_newstate(countingAlloc, host);
    if (!L)
    {
        delete host;
        return nullptr;
    }

    lua_Callbacks* callbacks = lua_callbacks(L);
    callbacks->userdata = host;
    callbacks->panic = panicHandler;
    callbacks->interrupt = interruptHandler;

    for (int i = 0; i < BodyCount; i++)
    {
        lua_pushcfunction(L, bodyEntries[i].function, bodyEntries[i].name);
        host->bodies[i] = lua_ref(L, -1);
        lua_pop(L, 1);
    }

    return L;
}

// Takes the main state. Any thread may call it once no thread runs the state.
SL_API void sl_close(lua_State* L)
{
    HostState* host = hostOf(L);
    lua_close(L);
    delete host;
}

// Bytes the state holds. Safe from any thread.
SL_API int64_t sl_memory(lua_State* L)
{
    return hostOf(L)->bytes.load(std::memory_order_relaxed);
}

SL_API int sl_openlibs(lua_State* L)
{
    return protect(L, OpenLibsBody, nullptr, 0);
}

// Pushes the chunk as a function. A compile error travels in the bytecode and
// comes back here as the message.
SL_API int sl_load(lua_State* L, const char* chunkName, const char* bytecode, size_t size)
{
    Chunk chunk = {chunkName, bytecode, size};
    return protect(L, LoadBody, &chunk, 1);
}

SL_API int sl_pushhostfunction(lua_State* L, HostFunction function, const char* debugName)
{
    HostClosure closure = {function, debugName};
    return protect(L, PushHostFunctionBody, &closure, 1);
}

// Even a number is checked for room. No protected call is made, so this one
// also works on a thread that has yielded.
SL_API int sl_pushnumber(lua_State* L, double value)
{
    if (!lua_checkstack(L, 1))
        return refuse(L, 0, stackOverflow);

    lua_pushnumber(L, value);
    return LUA_OK;
}

SL_API int sl_pushstring(lua_State* L, const char* data, size_t length)
{
    Text text = {data, length};
    return protect(L, PushStringBody, &text, 1);
}

// Pushes a new thread on L and hands back its state. The thread lives while
// something refers to it.
SL_API int sl_newthread(lua_State* L, lua_State** thread)
{
    *thread = nullptr;
    if (!lua_checkstack(L, 1))
        return refuse(L, 0, stackOverflow);

    *thread = lua_newthread(L);
    return LUA_OK;
}

SL_API int sl_createtable(lua_State* L, int arraySize, int recordSize)
{
    TableSize size = {arraySize, recordSize};
    return protect(L, CreateTableBody, &size, 1);
}

SL_API int sl_newuserdata(lua_State* L, size_t size, int tag, void** block)
{
    UserdataRequest request = {size, tag, nullptr};
    int status = protect(L, NewUserdataBody, &request, 1);
    *block = status == LUA_OK ? request.block : nullptr;
    return status;
}

// lua_getfield can run an __index metamethod.
SL_API int sl_getfield(lua_State* L, int index, const char* key)
{
    if (const char* reason = whyNot(L, 3))
        return refuse(L, 0, reason);

    index = lua_absindex(L, index);
    pushBody(L, GetFieldBody);
    lua_pushlightuserdata(L, const_cast<char*>(key));
    lua_pushvalue(L, index);
    return lua_pcall(L, 2, 1, 0);
}

// Takes the value on top, as lua_setfield does. It can run a __newindex
// metamethod, and a frozen table refuses.
SL_API int sl_setfield(lua_State* L, int index, const char* key)
{
    if (const char* reason = whyNot(L, 4))
        return refuse(L, 1, reason);

    index = lua_absindex(L, index);
    pushBody(L, SetFieldBody);
    lua_pushlightuserdata(L, const_cast<char*>(key));
    lua_pushvalue(L, index);
    lua_pushvalue(L, -4);
    int status = lua_pcall(L, 3, 0, 0);

    // The value is still under what the call left.
    lua_remove(L, status == LUA_OK ? -1 : -2);
    return status;
}

// Pushes the value as text, through __tostring when it has one.
SL_API int sl_tostring(lua_State* L, int index)
{
    if (const char* reason = whyNot(L, 2))
        return refuse(L, 0, reason);

    index = lua_absindex(L, index);
    pushBody(L, ToStringBody);
    lua_pushvalue(L, index);
    return lua_pcall(L, 1, 1, 0);
}

SL_API int sl_call(lua_State* L, int arguments, int results)
{
    if (const char* reason = whyNot(L, results - (arguments + 1)))
        return refuse(L, arguments + 1, reason);

    return lua_pcall(L, arguments, results, 0);
}

SL_API int sl_resume(lua_State* thread, lua_State* from, int arguments)
{
    return lua_resume(thread, from, arguments);
}

// For a host function: `return sl_fail(L, message, length);`. Whatever
// happens, an error value ends up on top for callHost to raise.
SL_API int sl_fail(lua_State* L, const char* message, size_t length)
{
    sl_pushstring(L, message, length);
    return SL_HOST_ERROR;
}

// Any thread may call this while the caller keeps the state from being
// closed. The script stops at its next loop or call with an error, and again
// after every pcall that catches it, until the host disarms.
SL_API void sl_interrupt_arm(lua_State* L)
{
    hostOf(L)->stopRequested.store(1, std::memory_order_relaxed);
}

SL_API void sl_interrupt_disarm(lua_State* L)
{
    hostOf(L)->stopRequested.store(0, std::memory_order_relaxed);
}

// Frees what sl_compile returned. The block came from this library's C
// runtime, so only this library can free it.
SL_API void sl_free(void* block)
{
    free(block);
}
