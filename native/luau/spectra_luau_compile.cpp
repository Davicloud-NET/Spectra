// The compiler half of the shim. It is a file of its own so that a program
// which never compiles links neither it nor Luau's compiler.

#include "spectra_luau.h"

#include "luacode.h"

// Returns the bytecode, to be freed with sl_free, or null when the compiler
// itself failed. A script with a mistake in it still compiles: the bytecode
// then carries the message and sl_load reports it.
//
// With `vectorLibrary` and `vectorConstructor`, say "Vector3" and "new", a
// call of that global with three numbers makes a vector with no call, and
// the library's name is also a name for the vector type. Null leaves both out.
SL_API char* sl_compile(
    const char* source,
    size_t size,
    int optimizationLevel,
    int debugLevel,
    const char* vectorLibrary,
    const char* vectorConstructor,
    size_t* bytecodeSize)
{
    sl_apply_flags();
    *bytecodeSize = 0;

    // Filled here to keep its layout out of the binding. It grows between Luau releases.
    lua_CompileOptions options = {};
    options.optimizationLevel = optimizationLevel;
    options.debugLevel = debugLevel;

    if (vectorLibrary && vectorConstructor)
    {
        options.vectorLib = vectorLibrary;
        options.vectorCtor = vectorConstructor;
        options.vectorType = vectorLibrary;
    }

    // The compiler is ordinary C++ and may throw, for one when memory runs
    // out. An exception must not reach the caller's managed frame.
    try
    {
        return luau_compile(source ? source : "", size, &options, bytecodeSize);
    }
    catch (...)
    {
        return nullptr;
    }
}
