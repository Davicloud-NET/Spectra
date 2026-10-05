// The compiler half of the shim. Kept apart so a game that ships only
// bytecode links neither this file nor Luau.Compiler.

#include "luacode.h"

#include <string.h>

#ifndef SL_API
#define SL_API extern "C"
#endif

// lua_CompileOptions grows between Luau releases. Filling it here keeps its
// layout out of the managed binding. Free the result with sl_free.
SL_API char* sl_compile(const char* source, size_t size, int optimizationLevel, int debugLevel, int typeInfoLevel, size_t* outsize)
{
    lua_CompileOptions options;
    memset(&options, 0, sizeof(options));
    options.optimizationLevel = optimizationLevel;
    options.debugLevel = debugLevel;
    options.typeInfoLevel = typeInfoLevel;
    return luau_compile(source, size, &options, outsize);
}
