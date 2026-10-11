#pragma once

// The Luau release external/luau is pinned to. Move it with the submodule.
// The build scripts and CI compare it with the submodule's tag, and the
// binding compares it with its own copy at startup.
#define SL_LUAU_RELEASE 742

// Raise it with every change to what this library does or exports, and
// Shim.Revision with it. A library built before the change has every other
// constant right, and the startup check tells it apart by this one.
#define SL_SHIM_REVISION 1

#ifndef SL_API
#define SL_API extern "C"
#endif

// What a host function returns instead of a result count. With an error, the
// error value is on top of the stack.
#define SL_HOST_ERROR (-1) // failed: text gets the calling script's position in front
#define SL_HOST_RAISE (-2) // failed: the value is raised as it is
#define SL_HOST_YIELD (-3) // SL_HOST_YIELD - n yields the top n values

// Sets Luau's feature flags once. Whatever makes a state or compiles calls it first.
void sl_apply_flags();
