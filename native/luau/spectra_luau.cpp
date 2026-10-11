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

// A thread's data holds two 32-bit numbers of the binding's.
static_assert(sizeof(void*) == 8, "The shim expects 64-bit pointers.");

// What a panic or a failed allocation left, for a debugger or a crash dump.
// Nothing is printed: a game has no console. Not static, so the writes stay.
char sl_fatal_message[512];

namespace
{

enum Body
{
    SetupBody,
    OpenLibsBody,
    SandboxBody,
    LoadBody,
    PushHostFunctionBody,
    PushHostCallBody,
    PushStringBody,
    CreateTableBody,
    NewUserdataBody,
    SetMetatableBody,
    NewObjectBody,
    SpawnBody,
    KeepBody,
    GetFieldBody,
    SetFieldBody,
    RawSetIndexBody,
    ToStringBody,
    BodyCount,
};

struct HostState
{
    // Written by the thread that runs the state, read by any.
    std::atomic<int64_t> bytes{0};

    // Bytes the state may hold, or 0 for no limit. Written by any thread.
    std::atomic<int64_t> budget{0};

    // (serial << 1) | running, for the outermost call into Luau. Written by
    // the thread that runs the state, read by the watchdog.
    std::atomic<uint64_t> call{2};

    // The serial of the call to stop, or 0. Written by any thread.
    std::atomic<uint32_t> stop{0};

    // How many calls into Luau are on the C stack.
    int depth = 0;

    // Why the interrupt raised in this call, until the host takes it.
    int stopReason = 0;

    // The binding's handle to its host object. Never read here.
    void* managed = nullptr;

    // The thread whose host function is running, for lua_resume's count of C calls.
    lua_State* current = nullptr;

    // Holds what the last thread to end returned.
    lua_State* results = nullptr;

    // Registry references.
    int bodies[BodyCount] = {};
    int closeThread = 0; // coroutine.close
    int objects = 0;     // slot -> the userdata of a managed object, weak

    bool objectTags[LUA_UTAG_LIMIT] = {};

    // (slot << 32) | generation of each object userdata the collector freed.
    uint64_t* released = nullptr;
    size_t releasedCount = 0;
    size_t releasedCapacity = 0;
};

typedef int (*HostFunction)(lua_State* L);
typedef int (*HostCall)(lua_State* L, void* host, int id);

struct Chunk
{
    const char* name;
    const char* bytecode;
    size_t size;
};

struct HostClosure
{
    void* function;
    const char* debugName;
    int id;
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

// What the userdata of a managed object holds.
struct ObjectPayload
{
    uint32_t slot;
    uint32_t generation;
};

struct ObjectRequest
{
    int tag;
    ObjectPayload payload;
};

struct ThreadRequest
{
    int like;
    int sandbox;
    lua_State* thread;
    int reference;
};

struct MetatableRequest
{
    int tag;
    int releases;
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
        // call can have a managed frame under it. So there is no limit here.
        // The budget is checked in the interrupt, and a system with no memory
        // left ends the process.
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

uint32_t serialOf(HostState* host)
{
    return static_cast<uint32_t>(host->call.load(std::memory_order_relaxed) >> 1);
}

// Counts the calls into Luau that are on the C stack. The outermost one gets
// a serial, which is what the watchdog times and what it asks to stop. No
// raise crosses a function that holds one: each makes a protected call.
struct CallScope
{
    HostState* host;

    explicit CallScope(HostState* host) : host(host)
    {
        if (host->depth++ > 0)
            return;

        host->stopReason = 0;
        uint64_t serial = (host->call.load(std::memory_order_relaxed) >> 1) + 1;
        host->call.store((serial << 1) | 1, std::memory_order_relaxed);
    }

    ~CallScope()
    {
        if (--host->depth == 0)
            host->call.store(host->call.load(std::memory_order_relaxed) & ~uint64_t(1), std::memory_order_relaxed);
    }
};

// A full collection first: what the allocator counts includes garbage, and
// the budget is for what scripts hold.
bool isOverBudget(lua_State* L, HostState* host)
{
    int64_t budget = host->budget.load(std::memory_order_relaxed);
    if (budget <= 0 || host->bytes.load(std::memory_order_relaxed) <= budget)
        return false;

    lua_gc(L, LUA_GCCOLLECT, 0);
    return host->bytes.load(std::memory_order_relaxed) > budget;
}

// Installed for the life of the state. Another thread only stores a serial,
// so it never writes into the VM's own data. Once this has raised it raises
// again at every loop and call, so a pcall in the script cannot hold on,
// until the call ends or the host takes the reason.
void interruptHandler(lua_State* L, int gc)
{
    // gc >= 0 means the collector is running. Raising is not allowed there.
    if (gc >= 0)
        return;

    HostState* host = hostOf(L);
    if (host->stopReason == 0)
    {
        uint32_t stop = host->stop.load(std::memory_order_relaxed);
        if (stop != 0 && stop == serialOf(host))
            host->stopReason = SL_STOP_TIME;
        else if (isOverBudget(L, host))
            host->stopReason = SL_STOP_MEMORY;
        else
            return;
    }

    lua_rawcheckstack(L, 2);
    pushPosition(L);
    lua_pushstring(L, host->stopReason == SL_STOP_MEMORY ? "script stopped by the host: out of memory" : "script stopped by the host");
    lua_concat(L, 2);
    lua_error(L);
}

// A thread a script makes belongs to the script that made it: it gets the
// low half of its maker's data. The high half names one thread.
void threadCreated(lua_State* parent, lua_State* thread)
{
    if (parent)
        lua_setthreaddata(thread, reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(lua_getthreaddata(parent)) & 0xffffffffu));
}

// Runs inside the collector, so it may not touch the VM. The host asks for
// the list later.
void objectCollected(lua_State* L, void* data)
{
    HostState* host = hostOf(L);
    if (host->releasedCount == host->releasedCapacity)
    {
        size_t capacity = host->releasedCapacity == 0 ? 64 : host->releasedCapacity * 2;
        void* grown = realloc(host->released, capacity * sizeof(uint64_t));
        if (!grown)
        {
            keepFatalMessage("luau: out of memory", nullptr);
            abort();
        }

        host->released = static_cast<uint64_t*>(grown);
        host->releasedCapacity = capacity;
    }

    const ObjectPayload* payload = static_cast<const ObjectPayload*>(data);
    host->released[host->releasedCount++] = (static_cast<uint64_t>(payload->slot) << 32) | payload->generation;
}

// What a host function returned, once its managed frame is gone. A raise from
// here crosses native frames only.
int finishHostCall(lua_State* L, int result)
{
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

// What Luau sees for a managed function that was handed over as a pointer.
int callHost(lua_State* L)
{
    HostState* host = hostOf(L);
    HostFunction function = reinterpret_cast<HostFunction>(lua_tolightuserdata(L, lua_upvalueindex(1)));

    lua_State* outer = host->current;
    host->current = L;
    int result = function(L);
    host->current = outer;

    return finishHostCall(L, result);
}

// The same for the binding's one entry point, which finds its delegate by the id.
int callHostById(lua_State* L)
{
    HostState* host = hostOf(L);
    HostCall function = reinterpret_cast<HostCall>(lua_tolightuserdata(L, lua_upvalueindex(1)));
    int id = static_cast<int>(reinterpret_cast<intptr_t>(lua_tolightuserdata(L, lua_upvalueindex(2))));

    lua_State* outer = host->current;
    host->current = L;
    int result = function(L, host->managed, id);
    host->current = outer;

    return finishHostCall(L, result);
}

// Runs once, when the state is made.
int setupBody(lua_State* L)
{
    HostState* host = hostOf(L);

    host->results = lua_newthread(L);
    lua_ref(L, -1);
    lua_pop(L, 1);

    lua_newtable(L);
    lua_newtable(L);
    lua_pushliteral(L, "v");
    lua_setfield(L, -2, "__mode");
    lua_setmetatable(L, -2);
    host->objects = lua_ref(L, -1);
    lua_pop(L, 1);
    return 0;
}

int openLibsBody(lua_State* L)
{
    luaL_openlibs(L);

    lua_getglobal(L, "coroutine");
    lua_getfield(L, -1, "close");
    hostOf(L)->closeThread = lua_ref(L, -1);
    return 0;
}

// Makes a table read-only, with every table it holds and every metatable on
// the way. One that is read-only already counts as done, which ends a cycle.
// Raises, so it runs under a protected call only.
void freezeDeep(lua_State* L, int index, int depth)
{
    if (!lua_istable(L, index) || lua_getreadonly(L, index))
        return;
    if (depth > 100)
        luaL_error(L, "tables are nested too deep to freeze");

    luaL_checkstack(L, 3, "too many values to freeze a table");
    index = lua_absindex(L, index);
    lua_setreadonly(L, index, 1);

    if (lua_getmetatable(L, index))
    {
        freezeDeep(L, -1, depth + 1);
        lua_pop(L, 1);
    }

    for (int cursor = lua_rawiter(L, index, 0); cursor >= 0; cursor = lua_rawiter(L, index, cursor))
    {
        freezeDeep(L, -1, depth + 1);
        lua_pop(L, 2);
    }
}

// luaL_sandbox freezes the tables that are globals, and not what they hold.
// A table inside a host's table would stay writable for every script.
int sandboxBody(lua_State* L)
{
    for (int cursor = lua_rawiter(L, LUA_GLOBALSINDEX, 0); cursor >= 0; cursor = lua_rawiter(L, LUA_GLOBALSINDEX, cursor))
    {
        freezeDeep(L, -1, 0);
        lua_pop(L, 2);
    }

    luaL_sandbox(L);
    return 0;
}

// Arguments: the request, then the table.
int setMetatableBody(lua_State* L)
{
    const MetatableRequest* request = static_cast<const MetatableRequest*>(lua_tolightuserdata(L, 1));
    freezeDeep(L, 2, 0);
    lua_settop(L, 2);
    lua_setuserdatametatable(L, request->tag);

    if (request->releases)
    {
        lua_setuserdatadtor(L, request->tag, objectCollected);
        hostOf(L)->objectTags[request->tag] = true;
    }
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
    lua_pushlightuserdata(L, closure->function);
    lua_pushcclosure(L, callHost, closure->debugName, 1);
    return 1;
}

int pushHostCallBody(lua_State* L)
{
    const HostClosure* closure = static_cast<const HostClosure*>(lua_tolightuserdata(L, 1));
    lua_pushlightuserdata(L, closure->function);
    lua_pushlightuserdata(L, reinterpret_cast<void*>(static_cast<intptr_t>(closure->id)));
    lua_pushcclosure(L, callHostById, closure->debugName, 2);
    return 1;
}

void pushText(lua_State* L, const Text* text)
{
    lua_pushlstring(L, text->data ? text->data : "", text->length);
}

int pushStringBody(lua_State* L)
{
    pushText(L, static_cast<const Text*>(lua_tolightuserdata(L, 1)));
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

bool hasMetatable(lua_State* L, int tag)
{
    lua_getuserdatametatable(L, tag);
    bool found = !lua_isnil(L, -1);
    lua_pop(L, 1);
    return found;
}

int newUserdataBody(lua_State* L)
{
    UserdataRequest* request = static_cast<UserdataRequest*>(lua_tolightuserdata(L, 1));
    if (static_cast<unsigned>(request->tag) >= LUA_UTAG_LIMIT)
        luaL_error(L, "userdata tag %d is out of range", request->tag);
    if (hostOf(L)->objectTags[request->tag])
        luaL_error(L, "userdata tag %d stands for managed objects", request->tag);

    request->block = hasMetatable(L, request->tag)
        ? lua_newuserdatataggedwithmetatable(L, request->size, request->tag)
        : lua_newuserdatatagged(L, request->size, request->tag);
    memset(request->block, 0, request->size);
    return 1;
}

// Makes the userdata of a managed object and files it under its slot.
int newObjectBody(lua_State* L)
{
    const ObjectRequest* request = static_cast<const ObjectRequest*>(lua_tolightuserdata(L, 1));
    void* block = lua_newuserdatataggedwithmetatable(L, sizeof(ObjectPayload), request->tag);
    memcpy(block, &request->payload, sizeof(ObjectPayload));

    lua_getref(L, hostOf(L)->objects);
    lua_pushvalue(L, -2);
    lua_rawseti(L, -2, static_cast<int>(request->payload.slot) + 1);
    lua_pop(L, 1);
    return 1;
}

// Makes a thread and keeps it by a reference, so nothing has to stay on a stack.
int spawnBody(lua_State* L)
{
    ThreadRequest* request = static_cast<ThreadRequest*>(lua_tolightuserdata(L, 1));
    lua_State* thread = lua_newthread(L);

    if (request->like != 0)
    {
        lua_getref(L, request->like);
        if (!lua_isthread(L, -1))
            luaL_error(L, "the reference is not a thread");

        lua_getfenv(L, -1);
        lua_xmove(L, thread, 1);
        lua_replace(thread, LUA_GLOBALSINDEX);
        lua_pop(L, 1);
    }

    if (request->sandbox)
        luaL_sandboxthread(thread);

    request->thread = thread;
    request->reference = lua_ref(L, 2);
    return 0;
}

// Arguments: where the reference goes, then the value.
int keepBody(lua_State* L)
{
    *static_cast<int*>(lua_tolightuserdata(L, 1)) = lua_ref(L, 2);
    return 0;
}

// Arguments: the key, then the object.
int getFieldBody(lua_State* L)
{
    pushText(L, static_cast<const Text*>(lua_tolightuserdata(L, 1)));
    lua_gettable(L, 2);
    return 1;
}

// Arguments: the key, the object, then the value.
int setFieldBody(lua_State* L)
{
    pushText(L, static_cast<const Text*>(lua_tolightuserdata(L, 1)));
    lua_insert(L, 3);
    lua_settable(L, 2);
    return 0;
}

// Arguments: the index, the table, then the value.
int rawSetIndexBody(lua_State* L)
{
    int index = static_cast<int>(reinterpret_cast<intptr_t>(lua_tolightuserdata(L, 1)));
    luaL_checktype(L, 2, LUA_TTABLE);
    lua_rawseti(L, 2, index);
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
    {setupBody, "sl_newstate"},
    {openLibsBody, "sl_openlibs"},
    {sandboxBody, "sl_sandbox"},
    {loadBody, "sl_load"},
    {pushHostFunctionBody, "sl_pushhostfunction"},
    {pushHostCallBody, "sl_pushhostcall"},
    {pushStringBody, "sl_pushstring"},
    {createTableBody, "sl_createtable"},
    {newUserdataBody, "sl_newuserdata"},
    {setMetatableBody, "sl_setuserdatametatable"},
    {newObjectBody, "sl_pushobject"},
    {spawnBody, "sl_spawn"},
    {keepBody, "sl_keep"},
    {getFieldBody, "sl_getfield"},
    {setFieldBody, "sl_setfield"},
    {rawSetIndexBody, "sl_rawseti"},
    {toStringBody, "sl_tostring"},
};

const char stackOverflow[] = "stack overflow";
const char badIndex[] = "no value at that stack index";

// Leaves the message where the result would be. The values the call would
// have taken go first. Room is made past the frame's limit, which a push
// would raise on, so no other value has to go.
int refuse(lua_State* L, int consumed, const char* message)
{
    int top = lua_gettop(L);
    lua_pop(L, consumed < top ? consumed : top);
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

// Luau checks an index in a debug build only. A negative one past the bottom
// of the frame reads below it.
bool isIndex(lua_State* L, int index)
{
    if (index > 0)
        return true;
    if (index < 0 && index > LUA_REGISTRYINDEX)
        return -index <= lua_gettop(L);
    return index == LUA_REGISTRYINDEX || index == LUA_GLOBALSINDEX;
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
    SL_CONSTANT(SL_REFUSED),
    SL_CONSTANT(SL_STOP_TIME),
    SL_CONSTANT(SL_STOP_MEMORY),

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

    SL_CONSTANT(LUA_CORUN),
    SL_CONSTANT(LUA_COSUS),
    SL_CONSTANT(LUA_CONOR),
    SL_CONSTANT(LUA_COFIN),
    SL_CONSTANT(LUA_COERR),

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

// Takes the main state. Any thread may call it once no thread runs the state.
SL_API void sl_close(lua_State* L)
{
    HostState* host = hostOf(L);
    lua_close(L);
    free(host->released);
    delete host;
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
    callbacks->userthread = threadCreated;

    for (int i = 0; i < BodyCount; i++)
    {
        lua_pushcfunction(L, bodyEntries[i].function, bodyEntries[i].name);
        host->bodies[i] = lua_ref(L, -1);
        lua_pop(L, 1);
    }

    if (protect(L, SetupBody, nullptr, 0) != LUA_OK)
    {
        sl_close(L);
        return nullptr;
    }

    return L;
}

// Bytes the state holds. Safe from any thread.
SL_API int64_t sl_memory(lua_State* L)
{
    return hostOf(L)->bytes.load(std::memory_order_relaxed);
}

// Bytes the state may hold before the running script is stopped, or 0 for no
// limit. Safe from any thread.
SL_API void sl_setbudget(lua_State* L, int64_t bytes)
{
    hostOf(L)->budget.store(bytes, std::memory_order_relaxed);
}

// The binding's own pointer. The shim hands it to callHostById and never reads it.
SL_API void sl_sethost(lua_State* L, void* host)
{
    hostOf(L)->managed = host;
}

SL_API void* sl_host(lua_State* L)
{
    return hostOf(L)->managed;
}

// The thread that holds what the last thread to end returned.
SL_API lua_State* sl_results(lua_State* L)
{
    return hostOf(L)->results;
}

SL_API int sl_openlibs(lua_State* L)
{
    return protect(L, OpenLibsBody, nullptr, 0);
}

// Makes the libraries and the globals read-only. Takes the main state, once
// the host has put its own functions and tables there.
SL_API int sl_sandbox(lua_State* L)
{
    return protect(L, SandboxBody, nullptr, 0);
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
    HostClosure closure = {reinterpret_cast<void*>(function), debugName, 0};
    return protect(L, PushHostFunctionBody, &closure, 1);
}

// The function is called with the pointer given to sl_sethost and this id.
SL_API int sl_pushhostcall(lua_State* L, HostCall function, const char* debugName, int id)
{
    HostClosure closure = {reinterpret_cast<void*>(function), debugName, id};
    return protect(L, PushHostCallBody, &closure, 1);
}

SL_API int sl_pushnil(lua_State* L)
{
    if (!lua_checkstack(L, 1))
        return refuse(L, 0, stackOverflow);

    lua_pushnil(L);
    return LUA_OK;
}

SL_API int sl_pushboolean(lua_State* L, int value)
{
    if (!lua_checkstack(L, 1))
        return refuse(L, 0, stackOverflow);

    lua_pushboolean(L, value);
    return LUA_OK;
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

SL_API int sl_pushvector(lua_State* L, float x, float y, float z)
{
    if (!lua_checkstack(L, 1))
        return refuse(L, 0, stackOverflow);

    lua_pushvector(L, x, y, z);
    return LUA_OK;
}

SL_API int sl_pushstring(lua_State* L, const char* data, size_t length)
{
    Text text = {data, length};
    return protect(L, PushStringBody, &text, 1);
}

// Pushes a copy of the value at the index.
SL_API int sl_pushvalue(lua_State* L, int index)
{
    if (!isIndex(L, index))
        return refuse(L, 0, badIndex);
    if (!lua_checkstack(L, 1))
        return refuse(L, 0, stackOverflow);

    lua_pushvalue(L, index);
    return LUA_OK;
}

// Pushes the thread itself, as a value.
SL_API int sl_pushthread(lua_State* L)
{
    if (!lua_checkstack(L, 1))
        return refuse(L, 0, stackOverflow);

    lua_pushthread(L);
    return LUA_OK;
}

// Pops so many values, or all there are.
SL_API void sl_pop(lua_State* L, int count)
{
    int top = lua_gettop(L);
    lua_settop(L, count >= top ? 0 : top - (count > 0 ? count : 0));
}

// Moves the top values of one thread to another thread of the same state.
// This is how a value gets to a thread that has yielded, where nothing that
// runs protected can make one.
SL_API int sl_xmove(lua_State* from, lua_State* to, int count)
{
    if (from == to || count < 0 || count > lua_gettop(from))
        return refuse(from, 0, "there are not so many values to move");
    if (!lua_checkstack(to, count))
        return refuse(from, count, stackOverflow);

    lua_xmove(from, to, count);
    return LUA_OK;
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

// Makes a thread and a reference that keeps it. With `like`, the reference of
// another thread, it shares that thread's globals. With `sandbox` it gets
// globals of its own that read through to the ones it would have had.
SL_API int sl_spawn(lua_State* L, int like, int sandbox, lua_State** thread, int* reference)
{
    ThreadRequest request = {like, sandbox, nullptr, LUA_NOREF};
    int status = protect(L, SpawnBody, &request, 0);

    *thread = status == LUA_OK ? request.thread : nullptr;
    *reference = status == LUA_OK ? request.reference : LUA_NOREF;
    return status;
}

// Keeps the value at the index alive until lua_unref lets it go. The work is
// done on the main thread, so the value may sit on a thread that has yielded.
SL_API int sl_keep(lua_State* L, int index, int* reference)
{
    *reference = LUA_NOREF;
    if (!isIndex(L, index))
        return refuse(L, 0, badIndex);

    lua_State* main = lua_mainthread(L);
    if (const char* reason = whyNot(main, 3))
        return refuse(L, 0, reason);

    // L may be the main thread, and the two pushes below would move the index.
    index = lua_absindex(L, index);
    pushBody(main, KeepBody);
    lua_pushlightuserdata(main, reference);
    lua_xpush(L, main, index);
    if (lua_pcall(main, 2, 0, 0) == LUA_OK)
        return LUA_OK;

    *reference = LUA_NOREF;
    lua_pop(main, 1);
    return refuse(L, 0, "the value could not be kept");
}

// Pushes what a reference keeps. No protected call, so any thread will do.
SL_API int sl_getref(lua_State* L, int reference)
{
    if (!lua_checkstack(L, 1))
        return refuse(L, 0, stackOverflow);

    lua_getref(L, reference);
    return LUA_OK;
}

SL_API int sl_createtable(lua_State* L, int arraySize, int recordSize)
{
    TableSize size = {arraySize, recordSize};
    return protect(L, CreateTableBody, &size, 1);
}

// The block is zeroed. A tag with a metatable gives its userdata that
// metatable. A tag that stands for managed objects is refused: sl_pushobject
// makes those.
SL_API int sl_newuserdata(lua_State* L, size_t size, int tag, void** block)
{
    UserdataRequest request = {size, tag, nullptr};
    int status = protect(L, NewUserdataBody, &request, 1);
    *block = status == LUA_OK ? request.block : nullptr;
    return status;
}

// Takes the table on top and makes it the one metatable of every userdata
// with this tag. The table is frozen with all it holds, since every script
// shares it. With `releases`, the tag stands for managed objects:
// sl_pushobject makes its userdata, and the collector reports each one it frees.
SL_API int sl_setuserdatametatable(lua_State* L, int tag, int releases)
{
    if (lua_gettop(L) < 1 || !lua_istable(L, -1))
        return refuse(L, 1, "a userdata metatable must be a table");
    if (static_cast<unsigned>(tag) >= LUA_UTAG_LIMIT)
        return refuse(L, 1, "the userdata tag is out of range");
    if (const char* reason = whyNot(L, 3))
        return refuse(L, 1, reason);
    if (hasMetatable(L, tag))
        return refuse(L, 1, "the userdata tag already has a metatable");

    MetatableRequest request = {tag, releases};
    pushBody(L, SetMetatableBody);
    lua_pushlightuserdata(L, &request);
    lua_pushvalue(L, -3);
    int status = lua_pcall(L, 2, 0, 0);

    // The table is still under what the call left.
    lua_remove(L, status == LUA_OK ? -1 : -2);
    return status;
}

// Pushes the userdata that stands for a slot of the host's object table. One
// exists per slot at a time, so two reads of one object are equal in a
// script. When it is gone, a new one is made with `nextGeneration` and
// `created` is set, and a report about the old generation means nothing.
SL_API int sl_pushobject(lua_State* L, int tag, uint32_t slot, uint32_t generation, uint32_t nextGeneration, int* created)
{
    *created = 0;
    HostState* host = hostOf(L);
    if (static_cast<unsigned>(tag) >= LUA_UTAG_LIMIT || !host->objectTags[tag])
        return refuse(L, 0, "the userdata tag does not stand for managed objects");
    if (slot >= 0x7fffffffu)
        return refuse(L, 0, "the object slot is out of range");
    if (!lua_checkstack(L, 2))
        return refuse(L, 0, stackOverflow);

    lua_getref(L, host->objects);
    lua_rawgeti(L, -1, static_cast<int>(slot) + 1);
    const ObjectPayload* found = static_cast<const ObjectPayload*>(lua_touserdatatagged(L, -1, tag));
    if (found && found->slot == slot && found->generation == generation)
    {
        lua_remove(L, -2);
        return LUA_OK;
    }
    lua_pop(L, 2);

    ObjectRequest request = {tag, {slot, nextGeneration}};
    int status = protect(L, NewObjectBody, &request, 1);
    *created = status == LUA_OK ? 1 : 0;
    return status;
}

// Hands over up to `capacity` of the object userdata the collector has freed
// since the last call, each as (slot << 32) | generation. Returns how many.
SL_API int sl_takereleased(lua_State* L, uint64_t* buffer, int capacity)
{
    HostState* host = hostOf(L);
    int count = 0;
    while (count < capacity && host->releasedCount > 0)
        buffer[count++] = host->released[--host->releasedCount];
    return count;
}

// lua_gettable can run an __index metamethod.
SL_API int sl_getfield(lua_State* L, int index, const char* key, size_t length)
{
    if (!isIndex(L, index))
        return refuse(L, 0, badIndex);
    if (const char* reason = whyNot(L, 3))
        return refuse(L, 0, reason);

    Text text = {key, length};
    CallScope scope(hostOf(L));
    index = lua_absindex(L, index);
    pushBody(L, GetFieldBody);
    lua_pushlightuserdata(L, &text);
    lua_pushvalue(L, index);
    return lua_pcall(L, 2, 1, 0);
}

// Takes the value on top, as lua_setfield does. It can run a __newindex
// metamethod, and a frozen table refuses.
SL_API int sl_setfield(lua_State* L, int index, const char* key, size_t length)
{
    if (lua_gettop(L) < 1 || !isIndex(L, index))
        return refuse(L, 1, badIndex);
    if (const char* reason = whyNot(L, 4))
        return refuse(L, 1, reason);

    Text text = {key, length};
    CallScope scope(hostOf(L));
    index = lua_absindex(L, index);
    pushBody(L, SetFieldBody);
    lua_pushlightuserdata(L, &text);
    lua_pushvalue(L, index);
    lua_pushvalue(L, -4);
    int status = lua_pcall(L, 3, 0, 0);

    // The value is still under what the call left.
    lua_remove(L, status == LUA_OK ? -1 : -2);
    return status;
}

// Pushes t[n] with no metamethod. Something that is not a table gives nil.
SL_API int sl_rawgeti(lua_State* L, int index, int n)
{
    if (!isIndex(L, index))
        return refuse(L, 0, badIndex);
    if (!lua_checkstack(L, 1))
        return refuse(L, 0, stackOverflow);

    if (lua_istable(L, index))
        lua_rawgeti(L, index, n);
    else
        lua_pushnil(L);
    return LUA_OK;
}

// t[n] = the value on top, with no metamethod. Takes the value. A frozen
// table refuses.
SL_API int sl_rawseti(lua_State* L, int index, int n)
{
    if (lua_gettop(L) < 1 || !isIndex(L, index))
        return refuse(L, 1, badIndex);
    if (const char* reason = whyNot(L, 4))
        return refuse(L, 1, reason);

    index = lua_absindex(L, index);
    pushBody(L, RawSetIndexBody);
    lua_pushlightuserdata(L, reinterpret_cast<void*>(static_cast<intptr_t>(n)));
    lua_pushvalue(L, index);
    lua_pushvalue(L, -4);
    int status = lua_pcall(L, 3, 0, 0);

    lua_remove(L, status == LUA_OK ? -1 : -2);
    return status;
}

// One step of a walk over a table. Start with a cursor of 0. While there is
// a pair, pushes its key and its value and sets `next` to the cursor of the
// step after. At the end, and for something that is not a table, pushes
// nothing and sets `next` to -1.
SL_API int sl_rawiter(lua_State* L, int index, int cursor, int* next)
{
    *next = -1;
    if (!isIndex(L, index))
        return refuse(L, 0, badIndex);
    if (!lua_checkstack(L, 2))
        return refuse(L, 0, stackOverflow);

    if (cursor >= 0 && lua_istable(L, index))
        *next = lua_rawiter(L, index, cursor);
    return LUA_OK;
}

// Makes a table read-only for scripts and for the host alike.
SL_API int sl_freeze(lua_State* L, int index)
{
    if (!isIndex(L, index) || !lua_istable(L, index) || index == LUA_REGISTRYINDEX)
        return refuse(L, 0, "only a table can be frozen");

    lua_setreadonly(L, index, 1);
    return LUA_OK;
}

// Pushes the value as text, through __tostring when it has one.
SL_API int sl_tostring(lua_State* L, int index)
{
    if (!isIndex(L, index))
        return refuse(L, 0, badIndex);
    if (const char* reason = whyNot(L, 2))
        return refuse(L, 0, reason);

    CallScope scope(hostOf(L));
    index = lua_absindex(L, index);
    pushBody(L, ToStringBody);
    lua_pushvalue(L, index);
    return lua_pcall(L, 1, 1, 0);
}

SL_API int sl_call(lua_State* L, int arguments, int results)
{
    if (arguments < 0 || lua_gettop(L) < arguments + 1)
        return refuse(L, arguments + 1, "there is no function under the arguments");
    if (const char* reason = whyNot(L, results - (arguments + 1)))
        return refuse(L, arguments + 1, reason);

    CallScope scope(hostOf(L));
    return lua_pcall(L, arguments, results, 0);
}

// How many of `count` values are arguments for a thread that is to run: all
// of them for one that yielded or has its function, one less for a new thread
// that takes its function too. -1 when the thread is not waiting.
static int argumentsFor(lua_State* thread, lua_State* source, int count)
{
    int status = lua_status(thread);
    if (status == LUA_YIELD)
        return count;
    if (status != LUA_OK)
        return -1;

    // LUA_COFIN is also what a thread with no function reads as.
    int state = lua_costatus(source, thread);
    if (state == LUA_COFIN)
        return count - 1;
    return state == LUA_COSUS ? count : -1;
}

// A thread that ended with values on its stack reads as one that has not
// started. So they move to the results thread, and the thread is left empty.
static int takeResults(HostState* host, lua_State* thread)
{
    int returned = lua_gettop(thread);
    lua_settop(host->results, 0);
    if (returned > 0 && lua_checkstack(host->results, returned))
        lua_xmove(thread, host->results, returned);
    else
        returned = 0;

    lua_settop(thread, 0);
    return returned;
}

// Starts a thread or goes on with one that has yielded. Takes the top `count`
// values of `source`, another thread of the same state: the arguments, and
// under them the function when the thread is new and has none. SL_REFUSED
// when the thread was not waiting.
SL_API int sl_resume(lua_State* thread, lua_State* source, int count, int* results)
{
    *results = 0;
    if (thread == source || count < 0 || count > lua_gettop(source))
        return SL_REFUSED;

    HostState* host = hostOf(thread);
    int arguments = argumentsFor(thread, source, count);
    if (arguments < 0 || thread == host->results || !lua_checkstack(thread, count))
    {
        lua_pop(source, count);
        return SL_REFUSED;
    }

    // What it yielded is still there when the host did not take it.
    if (lua_status(thread) == LUA_YIELD)
        lua_settop(thread, 0);
    lua_xmove(source, thread, count);

    int status;
    {
        CallScope scope(host);
        status = lua_resume(thread, host->current, arguments);
    }

    // After an error the value is on the thread's stack, and its frames are
    // still there for sl_frame.
    if (status == LUA_YIELD)
        *results = lua_gettop(thread);
    else if (status == LUA_OK)
        *results = takeResults(host, thread);
    return status;
}

// Ends a thread that is waiting, as coroutine.close does, and leaves it
// empty. L is another thread to run that on, the main one for a host. A
// thread that is running cannot be ended, and the message says so.
SL_API int sl_cancel(lua_State* L, lua_State* thread)
{
    HostState* host = hostOf(L);
    if (L == thread || !lua_checkstack(thread, 1))
        return refuse(L, 0, "the thread cannot be ended from here");
    if (const char* reason = whyNot(L, 2))
        return refuse(L, 0, reason);

    CallScope scope(host);
    lua_getref(L, host->closeThread);
    lua_pushthread(thread);
    lua_xmove(thread, L, 1);
    return lua_pcall(L, 1, 0, 0);
}

// One frame of a thread's call stack, innermost first: 0 past the last
// frame, 1 for a function of a script, 2 for one of the host or of Luau.
// `source` is the chunk's name as it was loaded, and the texts live as long
// as the frame. A thread that ended in an error still has its frames.
SL_API int sl_frame(lua_State* L, int level, const char** source, const char** name, int* line)
{
    *source = nullptr;
    *name = nullptr;
    *line = 0;

    lua_Debug ar;
    if (level < 0 || !lua_getinfo(L, level, "sln", &ar))
        return 0;

    *name = ar.name;
    if (strcmp(ar.what, "C") == 0)
        return 2;

    *source = ar.source;
    *line = ar.currentline > 0 ? ar.currentline : 0;
    return 1;
}

// For a host function: `return sl_fail(L, message, length);`. Whatever
// happens, an error value ends up on top for callHost to raise.
SL_API int sl_fail(lua_State* L, const char* message, size_t length)
{
    sl_pushstring(L, message, length);
    return SL_HOST_ERROR;
}

// (serial << 1) | running, for the outermost call into Luau. Each such call
// gets the next serial. Safe from any thread while the caller keeps the state
// from being closed.
SL_API uint64_t sl_callstate(lua_State* L)
{
    return hostOf(L)->call.load(std::memory_order_relaxed);
}

// Asks the call with this serial to stop. Any thread may call this while the
// caller keeps the state from being closed. The script stops at its next
// loop or call with an error, and again after every pcall that catches it.
// A request for a call that has ended does nothing.
SL_API void sl_interrupt_arm(lua_State* L, uint32_t serial)
{
    hostOf(L)->stop.store(serial, std::memory_order_relaxed);
}

SL_API void sl_interrupt_disarm(lua_State* L)
{
    hostOf(L)->stop.store(0, std::memory_order_relaxed);
}

// Why the interrupt stopped a script in this call: SL_STOP_TIME,
// SL_STOP_MEMORY or 0. Taking the reason ends the stop, so whatever called
// the stopped script goes on, as a new call for the watchdog.
SL_API int sl_takestop(lua_State* L)
{
    HostState* host = hostOf(L);
    int reason = host->stopReason;
    if (reason == 0)
        return 0;

    host->stopReason = 0;
    host->stop.store(0, std::memory_order_relaxed);
    host->call.store(host->call.load(std::memory_order_relaxed) + 2, std::memory_order_relaxed);
    return reason;
}

// Frees what sl_compile returned. The block came from this library's C
// runtime, so only this library can free it.
SL_API void sl_free(void* block)
{
    free(block);
}
