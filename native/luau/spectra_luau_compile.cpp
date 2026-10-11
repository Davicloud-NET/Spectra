// The compiler half of the shim. It is a file of its own so that a program
// which never compiles links neither it nor Luau's compiler.

#include "spectra_luau.h"

#include "luacode.h"

// lua_CompileOptions grows between Luau releases. Filling it here keeps its
// layout out of the binding.
//
// Returns the bytecode, to be freed with sl_free, or null when the compiler
// itself failed. A script with a mistake in it still compiles: the bytecode
// then carries the message and sl_load reports it.
SL_API char* sl_compile(const char* source, size_t size, int optimizationLevel, int debugLevel, size_t* bytecodeSize)
{
    sl_apply_flags();
    *bytecodeSize = 0;

    lua_CompileOptions options = {};
    options.optimizationLevel = optimizationLevel;
    options.debugLevel = debugLevel;

    // The compiler is ordinary C++ and may throw, for one when memory runs
    // out. An exception must not reach the caller's managed frame.
    try
    {
        return luau_compile(source, size, &options, bytecodeSize);
    }
    catch (...)
    {
        return nullptr;
    }
}
