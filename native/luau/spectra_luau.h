#pragma once

// The Luau release external/luau is pinned to. Move it with the submodule.
// The build scripts compare it with the submodule's tag, and the binding
// compares it with its own copy at startup.
#define SL_LUAU_RELEASE 742

#ifndef SL_API
#define SL_API extern "C"
#endif

// What a host function returns instead of a result count.
#define SL_HOST_ERROR (-1) // failed, the error value is on top of the stack
#define SL_HOST_YIELD (-2) // SL_HOST_YIELD - n yields the top n values

// Sets Luau's feature flags once. Whatever makes a state or compiles calls it first.
void sl_apply_flags();
