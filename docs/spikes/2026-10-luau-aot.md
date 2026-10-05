# Luau in a NativeAOT executable, October 2026

Verdict: Luau embeds in a .NET 10 NativeAOT executable on Windows and on Linux. All ten checks pass on both, linked statically and as a shared library.
One rule comes out of it. A Luau error must be raised from native code, never from a managed frame. A small C++ shim does that. Without it the process dies at the next .NET garbage collection, or carries on with damaged objects.
The cost is small: about 29 ns for a call from C# into Luau and 17 to 21 ns for a call from Luau into C# on Windows, and about 200 KB of executable for the VM.

This is a spike. It produces answers, not a binding. Everything lives under
`spikes/luau-aot/`, nothing in the engine changed, and the project is not in
`Spectra.slnx`.

## Rig and versions

| | |
| --- | --- |
| Machine | 13th Gen Intel Core i9-13900K, 24 cores, 32 logical CPUs, 32 GB |
| Windows | Windows 11 Pro 10.0.26300 |
| Linux | Ubuntu 24.04.3 LTS in WSL2, kernel 6.6.87.2-microsoft-standard-WSL2 |
| Luau | tag `0.741`, commit `2d88f9b5facaa9e4ec91247b39ae8251bd49a244`, dated 2026-10-02 |
| .NET | SDK 10.0.401, runtime and ILCompiler 10.0.12, on both platforms |
| Windows C++ | Visual Studio Enterprise 2026 18.10, MSVC 19.51.36260 (toolset 14.51.36231), Windows SDK 10.0.26100.0, CMake 4.2.3 |
| Linux C++ | clang 18.1.3, GNU ld 2.42, glibc 2.39, libstdc++ 13.3.0 headers |

The machine was in use while the spike ran. Timing numbers are the best of nine
rounds, from a process pinned to logical CPUs 0 and 2. They come from the last
full run. Across three full runs the same number moved by 10 to 20 percent, so
read them to two digits at most.

Two things were downloaded. The Luau source, by `git clone` at the tag. And two
NuGet packages that the Linux publish restored into the user's NuGet cache:
`runtime.linux-x64.Microsoft.DotNet.ILCompiler` 10.0.12 and
`Microsoft.NETCore.App.Runtime.NativeAOT.linux-x64` 10.0.12. Nothing was
installed.

## What was built

`native/spectra_luau.cpp` is the shim, 334 lines. `LuauSpike/` is a C#
console project published with NativeAOT. It runs every check and prints PASS
or FAIL with numbers.

Luau was built three ways per platform, and the C# project five ways:

| Variant | What is in the executable |
| --- | --- |
| `static-full-longjmp` | VM, compiler, code generator. Errors raised with longjmp. The main configuration. |
| `static-full-cxx` | The same, with errors raised as C++ exceptions. |
| `static-compiler-longjmp` | VM and compiler. |
| `static-vm-longjmp` | VM only. Runs bytecode that another build compiled. |
| `shared-longjmp` | No Luau. `luau.dll` or `libluau.so` sits beside the executable. |

The header of every run prints `NativeAOT: True`. That line checks that the
assembly has no file on disk, because the dynamic code switch is also off in a
JIT run of a project that sets `PublishAot`.

## The ten checks

Both columns are the `static-full-longjmp` build unless a cell says otherwise.
All five variants pass on both platforms: 24 checks in the full builds, 23 with
no code generator, 21 in the VM-only build.

| # | Check | Windows x64 | Linux x64 |
| --- | --- | --- | --- |
| 1 | Compile a source string and run it. Load bytecode with no compiler linked. | PASS. 13 B of source became 39 B of bytecode and returned 42. A syntax error came back as a message. The VM-only build ran from `.luauc` files that the full build wrote. | PASS, same output |
| 2 | Call a Luau function from C# with arguments, read results | PASS. `add(2, 40)` gave 42. A string and a number came back from `describe`. | PASS |
| 3 | Call C# from Luau through `[UnmanagedCallersOnly]` pointers | PASS. Numbers and strings both ways, two results. | PASS |
| 4 | Errors in both directions, and unwinding | PASS. With the shim, 200 rounds under GC stress. Without it the child process dies with 0xC0000602 at the next GC. | PASS. Without the shim the child dies with SIGABRT, exit 134, at the next GC. |
| 5 | Userdata that refers to a managed object | PASS, both designs. GCHandle 119 ns per object, id table 59 ns. | PASS. 111 ns and 65 ns. |
| 6 | A script that yields, resumed from C# | PASS. 17.8 ns per resume. | PASS. 15.1 ns. |
| 7 | A runaway script stopped from the host | PASS. Returned 19 us and 106 us after the stop request. The state kept working. | PASS. 9 us and 9 us. |
| 8 | `luaL_sandbox` and one environment per script | PASS. 1,423 B per environment. An 8 MB memory limit held. | PASS, same numbers |
| 9 | Cost | PASS. C# to Luau 29.2 ns. Luau to C# 16.5 to 21.4 ns. 200 lines compile in 154 us and load in 4 us. A ready state is 254,576 B. The VM adds 201,728 B to the executable. | PASS. 25.4 ns. 12.6 to 17.8 ns. 111 us and 4 us. 254,576 B. 288,856 B. |
| 10 | Native code generation | PASS. Builds and runs. 4.8x on a float loop, 2.0x on a table loop, 1.19x on the gameplay script. | PASS. 4.4x, 2.5x, 1.23x. |

## Reproduce

From the repository root, in an ordinary shell. No Developer prompt.

```powershell
spikes/luau-aot/build-luau.ps1     # clones Luau 0.741 into spikes/luau-aot/.luau, builds it with MSVC
spikes/luau-aot/run-windows.ps1    # publishes the five variants, runs the checks, prints the sizes
```

Linux, driven from the same Windows machine:

```powershell
wsl -d Ubuntu -- bash /mnt/d/Projekte/Spectra/spikes/luau-aot/build-luau.sh
spikes/luau-aot/run-linux.ps1
```

Each variant is one publish. `run-windows.ps1` puts the Visual Studio Installer
folder on `PATH` first, as AGENTS.md asks.

```powershell
dotnet publish spikes/luau-aot/LuauSpike/LuauSpike.csproj -c Release -r win-x64 `
    -p:LuauLink=static-full -p:LuauUnwind=longjmp -o spikes/luau-aot/out/win-x64/static-full-longjmp
```

The Linux publish adds `-r linux-x64 -p:DisableUnsupportedError=true` and needs
`spikes/luau-aot/cross` on `PATH`. See the first trap below for why.

One crash case by hand:

```powershell
spikes/luau-aot/out/win-x64/static-full-longjmp/LuauSpike.exe --case=raise-in-managed-frame
```

Logs land in `out/<rid>/logs/`. `.luau/`, `build/` and `out/` are git-ignored.

## Check 4: errors and unwinding

### How Luau raises an error

It depends on the build, and `VM/src/ldo.cpp` has both.

- With `LUA_USE_LONGJMP=1`, `luaD_throw` calls `longjmp` to the nearest
  protected call. If there is none it calls the panic callback and then
  `abort`.
- Without it, `luaD_throw` throws a C++ `lua_exception`, caught in
  `luaD_rawrunprotected`. If there is no protected call the exception is never
  caught.

Luau's CMake option `LUAU_EXTERN_C` gives the API C names and turns longjmp on
in the same step. The default build has C++ names and C++ exceptions.

### Can an unwind cross a managed frame

Yes, with either build. It happens whenever managed code calls a Luau function
that raises. `lua_error` is the obvious one. The same goes for the `luaL_check`
family, for `lua_yield`, for anything that can run a metamethod such as
`lua_getfield`, and for anything that allocates if the allocator returns null.

The spike runs that case in a child process. A host function registered straight
as a `lua_CFunction` calls `lua_error`. The script catches the error with
`pcall`, then calls a second host function that allocates and calls
`GC.Collect()`.

| Build | Windows | Linux |
| --- | --- | --- |
| static, longjmp | dies at the collection, exit 0xC0000602 | dies at the collection, SIGABRT, exit 134 |
| static, C++ exceptions | survives the collection, then finds a string held by `Main` changed | dies at the collection, exit 134 |
| shared, longjmp | survives the collection, then finds a string held by `Main` changed | dies at the collection, exit 134 |
| any, with no collection before the call returns | 200 rounds survive. The `finally` block in the host function ran 0 times out of 200. | the same |

So the unwind itself works. `pcall` in the script sees the error and the script
goes on. The damage shows later, at the first collection. In two of the Windows
builds there was no crash even then. The process went on, and a string that
`Main` had been holding no longer had its contents.

The likely cause was not proven here. The runtime records where a thread left
managed code. The unwind skips the code that would reset that record, so the
collector starts its stack walk from a frame that no longer exists. It then
either fails or misses the frames above, and frees what they hold.

### The pattern that prevents it

Luau never sees a managed function. It sees one native trampoline in the shim,
`callHost`, with the managed function pointer as an upvalue. The managed
function returns a number:

- zero or more: that many results are on the stack
- `-1`: failure, and the error value is on top of the stack
- `-2 - n`: yield `n` values

The trampoline calls `lua_error` or `lua_yield` after the managed function has
returned. The unwind then crosses native frames only.

The managed side has two rules. It catches every exception at the boundary and
turns it into the `-1` return. And it calls no Luau function that can raise. For
a read that can run script code the shim has `sl_getfield`, which makes the call
under `lua_pcall` and returns a status.

With that in place the same scenario survives 200 rounds, and the `finally`
block runs 200 times. That is check 4d. Check 10 repeats a raise under 64 frames
of native code that Luau generated at run time, from Luau and from a host
function, and both are caught.

### The other two ways to die

| Case | Windows | Linux |
| --- | --- | --- |
| A managed exception leaves an `[UnmanagedCallersOnly]` function | fail fast, exit 0xC0000409, with a stack trace | abort, exit 134, with a stack trace |
| A raise with no protected call active, longjmp build | the panic callback prints the message, then abort, 0xC0000409 | the same, exit 134 |
| The same in the C++ exceptions build | 0xC0000409 and no output at all | `terminate` prints the message, exit 134 |

The second case is easy to hit. Reading a field of a script table from C# with
`lua_getfield` runs the table's `__index`, which can call `error`.

## Costs

All nanoseconds, `static-full-longjmp`, best of nine rounds.

| Measurement | Windows | Linux |
| --- | --- | --- |
| One P/Invoke (`lua_gettop`) | 2.19 | 1.48 |
| The same with `[SuppressGCTransition]` | 0.60 | 0.41 |
| C# calls Luau `add(a, b)` with six P/Invokes | 37.4 | 39.6 |
| The same, `SuppressGCTransition` on five of them | 27.8 | 25.3 |
| The same as one shim call | 29.2 | 25.4 |
| Luau calls C#, arguments unpacked by the shim | 16.8 | 12.6 |
| Luau calls C#, generic `int f(lua_State*)` | 21.4 | 17.8 |
| Generic, with `SuppressGCTransition` inside | 16.5 | 12.6 |
| Generic, registered without the trampoline | 18.9 | 15.6 |
| Luau calls a native C function | 12.8 | 8.3 |
| Luau calls a Luau function | 10.1 | 8.4 |
| One iteration of the empty loop around those calls | 1.86 | 2.02 |
| `pcall`, no error | 35 | 29 |
| `pcall` catching `error()` | 1457 | 75 |
| The same in the C++ exceptions build | 2749 | 1110 |
| `pcall` catching a host error code | 1501 | 96 |
| `pcall` catching a C# exception thrown five frames down | 2416 | 2829 |
| Resume a coroutine that yields again | 17.8 | 15.1 |
| Userdata with a GCHandle: create, one method call, collect | 119 | 111 |
| Userdata with an id: the same | 59 | 65 |
| One loop iteration, no interrupt callback | 2.50 | 2.76 |
| One loop iteration, interrupt callback installed | 3.36 | 3.09 |

How to read it:

- A call into C# costs about 4 ns more than a call into a C function that does
  the same work: 16.8 against 12.8 on Windows, 12.6 against 8.3 on Linux.
  Reverse P/Invoke is not the expensive part.
- The trampoline costs about 2.5 ns per call, the gap between the generic row
  and the row without it.
- In the generic shape the three calls back into Luau cost about 5 ns.
  `SuppressGCTransition` on them brings it level with the typed shape.
- From C#, `lua_pcall` is most of the cost. Folding six P/Invokes into one
  saves 8 to 14 ns.
- A caught error costs about 20 times more on Windows than on Linux in the
  longjmp build. A comment in Luau's `CodeGen.cpp` gives the reason: the Windows CRT
  unwinds the stack in `longjmp`.

Compile and load, for `scripts/gameplay_200.luau`, 200 lines and 5,467 B of
source. Medians of 201 runs.

| | Windows | Linux |
| --- | --- | --- |
| Compile at optimization level 1 | 154 us | 111 us |
| Compile at optimization level 2 | 214 us | 154 us |
| Bytecode size | 4,431 B | 4,431 B |
| `luau_load` | 4 us | 4 us |
| Load and run the top level | 4 us | 3 us |

Memory, counted by the shim's allocator. The numbers are the same on both
platforms.

| | Bytes |
| --- | --- |
| After `lua_newstate` | 150,248 |
| With the standard libraries | 254,576 |
| With ten host functions and `luaL_sandbox` | 254,576 |
| One more script environment: a thread with `luaL_sandboxthread`, mean of 1000 | 1,423 |

The third row equals the second because the new objects fit in pages the VM
already held.

Executable size, in bytes. The shared variant is the baseline: the same C#
program with no Luau inside.

| Variant | Windows | Linux |
| --- | --- | --- |
| `shared-longjmp`, the executable alone | 2,085,888 | 2,454,824 |
| `static-vm-longjmp` | 2,287,616 | 2,743,680 |
| `static-compiler-longjmp` | 2,813,952 | 3,374,832 |
| `static-full-longjmp` | 3,330,560 | 4,005,616 |
| `static-full-cxx` | 3,330,560 | 4,005,632 |
| `luau.dll` or `libluau.so`, everything in it | 1,536,512 | 2,097,064 |

So static linking adds:

| | Windows | Linux |
| --- | --- | --- |
| VM and shim | 201,728 | 288,856 |
| VM, shim and compiler | 728,064 | 920,008 |
| VM, shim, compiler and code generator | 1,244,672 | 1,550,792 |

The Linux executables were stripped by the publish, which is the default there.
The Windows numbers are the `.exe` without its `.pdb`.

The shared library costs calls. One P/Invoke into `luau.dll` took 5.84 ns in the
last run and 3.86 and 3.88 ns in the two before it, against 2.19 ns linked in.
On Linux it was 2.97 ns against 1.48 ns. `SuppressGCTransition` helps less
there: 2.38 ns on Windows and 2.24 ns on Linux.

## The other checks, briefly

Userdata. Both designs keep the managed object alive across a .NET collection
while Luau holds it, and let .NET collect it after Luau's collector has let go.
200,000 objects were created and dropped in each design and every one was
released. In the GCHandle design the destructor is a managed function that Luau
calls from inside its collector. In the id design the shim queues the ids of
collected userdata and the host drains the queue.

Coroutines. A script yields once with `coroutine.yield` and once through a host
function, `wait(0.5)`. C# resumes it twice, doing other Luau work in between,
and reads the final result. The host function yields by returning a code. It
cannot call `lua_yield` itself, because that function raises when the yield
would cross a C call.

Stopping a script. A watchdog thread arms the interrupt callback after 50 ms.
The handler is in the shim and raises "script stopped by host". It stays armed
until the host disarms it, so a script that catches the error with `pcall` is
stopped again at its next loop. Check 7b is that script. `lua.h` says the
interrupt callback may be set from any thread.

Sandbox. `luaL_sandbox` freezes the shared globals and the library tables. Each
script gets a thread with `luaL_sandboxthread`. A global written by script A
survives to A's next run and script B never sees it. Writing `string.upper` or
`_G.host_add` fails. A script can shadow `print` in its own environment. A
script loaded straight into the frozen state cannot create a global.

Memory limit. The shim's allocator refuses to grow past 8,388,608 B. A script
that allocates forever stopped with `LUA_ERRMEM`, "not enough memory", at
8,372,680 B. After a collection the state was back to 336,376 B and ran scripts
again. See the recommendation about this below: it is only safe when the
failing allocation happens in a native frame.

## Check 10: native code generation

`Luau.CodeGen` builds with MSVC and with clang, links statically into the AOT
executable, and `luau_codegen_supported` returns 1 on this CPU on both
platforms. A host function confirmed with `lua_incustomexecution` that the
calling Luau function was running as native code.

| | Windows | Linux |
| --- | --- | --- |
| Mandelbrot, 256x256, 256 steps: interpreter | 58.1 ms | 54.1 ms |
| The same as native code | 12.2 ms, 4.8x | 12.3 ms, 4.4x |
| Sieve to 2,000,000: interpreter | 41.8 ms | 47.6 ms |
| The same as native code | 21.2 ms, 2.0x | 19.3 ms, 2.5x |
| `demo()` of the 200-line gameplay script: interpreter | 7.37 us | 6.96 us |
| The same as native code | 6.17 us, 1.19x | 5.67 us, 1.23x |
| Generating native code for the 200-line script | 1083 us | 775 us |
| Added to the executable | 516,608 B | 630,784 B |

It is not worth having now. It helps arithmetic in tight loops. Code that is
tables, closures, strings and calls into the host, which is what gameplay
scripts are, gained about 20 percent. Generating the code takes about 200 times
longer than loading the bytecode on Linux and 270 times on Windows. And it allocates executable memory at run time
(`VirtualAlloc` then `PAGE_EXECUTE_READ`, or `mmap` then `PROT_EXEC`), which is
the thing a NativeAOT build exists to do without. Adding it later is one more
static library and three functions, and it can be applied to one script at a
time.

## Surprises and traps

1. `~/spectra-dotnet10` in WSL is a runtime, not an SDK. `dotnet publish` cannot
   run there. The Linux executables were made on Windows instead. The IL
   compiler targets Linux from Windows without complaint. What is missing is a
   linker, so `cross/clang.cmd` and `cross/objcopy.cmd` pass the link and strip
   commands to the real tools in WSL, and `-p:DisableUnsupportedError=true`
   turns off the SDK's "Cross-OS native compilation is not supported" error.
   The result is an ordinary linux-x64 ELF, but `dotnet publish` on a Linux
   host was never run.
2. The WSL distro has clang 18 and no cmake, make or gcc. `build-luau.sh` calls
   `clang++` on the source folders directly, with the flags Luau's CMake
   Release build uses. `native/CMakeLists.txt` was only exercised with MSVC.
3. A JIT run hides the unwinding bug. Under CoreCLR on Windows the no-shim case
   survives all 200 rounds and its `finally` block runs 200 times. The same
   source published with NativeAOT dies at the first collection. A test suite
   started with `dotnet run` cannot see this class of bug. The check has to run
   against the published binary. To see it: `dotnet build` the project with
   `-c Release -r win-x64 -p:LuauLink=shared` and run the `LuauSpike.exe` under
   `bin/` with `--case=raise-in-managed-frame`.
4. Luau's default build is `/MD`. Linked into a NativeAOT executable that fails
   with `LNK2038: mismatch detected for 'RuntimeLibrary': value
   'MT_StaticRelease' doesn't match value 'MD_DynamicRelease'`. The static
   libraries must be built with `/MT`, which is Luau's `LUAU_STATIC_CRT=ON`.
   Configuring `native/` with `-DSPIKE_STATIC_CRT=OFF` reproduces the failure.
5. On Linux the AOT link leaves libstdc++ out. The full build then fails with
   1068 undefined references. Even the VM-only longjmp build fails, with five:
   `__cxa_begin_catch`, `std::terminate`, `__gxx_personality_v0`,
   `__cxa_guard_acquire` and `__cxa_guard_release`. The fix is
   `<LinkStandardCPlusPlusLibrary>true</LinkStandardCPlusPlusLibrary>` in the
   project. The executable then needs `libstdc++.so.6` and `libgcc_s.so.1` at
   run time, as `ldd` shows. Publishing with
   `-p:LinkStandardCPlusPlusLibrary=false` reproduces the failure.
6. `LUAU_EXTERN_C` is two switches in one. It gives the API C names and it turns
   longjmp on. To test C++ exceptions with C names the spike sets `LUA_API`,
   `LUACODE_API` and `LUACODEGEN_API` itself.
7. A caught error costs 1.5 us on Windows with longjmp and 2.7 us with C++
   exceptions. On Linux with longjmp it costs 75 ns. Scripts that use `pcall`
   for control flow will feel that on Windows and not on Linux.
8. An empty state is 150 KB and a usable one is 254 KB. One state per script
   would be expensive. One state with a thread per script is 1.4 KB each.
9. `luau_compile` returns memory from the library's `malloc`. The shim exports
   `sl_free` so the same C runtime frees it.
10. `lua_CompileOptions` is a struct of 14 ints and pointers that the compiler
    reads by layout, and the type tags in `lua.h` depend on a build option:
    `LUA_VECTOR_DOUBLE` moves `LUA_TVECTOR`. The spike keeps the struct inside
    the shim and compares eight constants with the library at startup, check 0.
    This is the same risk the Box3D ABI manifest guards against.
11. The AOT link target decides whether to relink by comparing timestamps of
    its inputs. Pointing `NativeLibrary` at another variant's files, which are
    older than the last executable, would not relink. This was read from the
    targets file, not observed. The project gives every variant its own `obj`
    folder so it cannot happen.
12. `%~dp0` in a `.cmd` is the current directory, not the script's folder, when
    MSBuild calls it by a quoted name found on `PATH`. The wrappers look their
    own folder up on `PATH` instead.
13. `wsl.exe` eats backslashes and quotes in arguments. The wrappers write the
    arguments to a file and a script on the Linux side turns `D:\a\b` into
    `/mnt/d/a/b`.
14. The repository has `* text=auto` and this machine has `core.autocrlf` on. A
    shell script checked out with CRLF does not run under bash, so
    `spikes/luau-aot/.gitattributes` pins `*.sh` to LF.
15. The dynamic code feature switch is off in a JIT run of a project that sets
    `PublishAot`. It is not proof of a NativeAOT build.

## Recommendations for the real binding

Static or shared. Link statically in published executables, and build the shared
library as well.

- The game links the VM and the shim only, 201,728 B on Windows. The compiler
  stays out with no second library to maintain. The editor adds the compiler.
- A call is 2 ns cheaper and there is no native file that has to sit beside the
  executable, which is the trap the Assimp spike found.
- Static linking only exists in an AOT publish. `dotnet run` and every test
  suite run under the JIT and need `luau.dll` or `libluau.so`. One build script
  should produce both from the same sources, as `build-luau.ps1` does, and the
  binding should use one library name for both. The spike's project file shows
  the switch: `DirectPInvoke` and `NativeLibrary` items when static, a copied
  file when shared.
- If two products are one too many, shared alone works. Every check passed with
  it. It costs about 2 ns per call and ships the compiler in the game unless a
  VM-only library is built too.

Exceptions or longjmp. Longjmp.

- It is what Luau's own `LUAU_EXTERN_C` option selects.
- A caught error costs 75 ns against 1110 ns on Linux, and 1457 ns against
  2749 ns on Windows.
- A raise with no protected call active reaches the panic callback with the
  message. In the C++ build on Windows it ended the process with no output.
- Nothing else differed. Both builds pass every check. They are the same size
  on Windows and 16 bytes apart on Linux.

Shim or no shim. A shim, and it is not optional. It should own:

- the one trampoline every managed function goes through
- every call that can raise, in a form that returns a status
- the interrupt handler, the allocator and the panic handler
- every Luau struct: compile options, callbacks, debug records. C# should never
  mirror their layout.
- a function that reports the constants C# has copied, checked at startup or in
  a test, like `Box3DAbiTests`

The crash cases should become tests that run against a published binary, not
under `dotnet run`. See trap 3.

How callbacks should be shaped.

- One signature for every host function: `[UnmanagedCallersOnly] static int
  F(lua_State*)`, returning a result count, `-1` for an error with the message
  pushed, or a yield code. A source generator can write these wrappers, as one
  already does for entities.
- `try` and `catch (Exception)` in every wrapper. An exception that gets out
  ends the process.
- Report expected failures with the error return, not with a C# exception. The
  exception adds about 0.9 us on Windows and 2.7 us on Linux.
- Read and push simple values through accessors marked `SuppressGCTransition`:
  `lua_tonumberx`, `lua_toboolean`, `lua_type`, `lua_touserdatatagged`,
  `lua_pushnumber`, `lua_pushboolean`, `lua_gettop`, `lua_settop`. They do not
  allocate, raise or call back. That brings the generic shape to 16.5 ns, level
  with a thunk written per signature.
- Keep `lua_pcall`, `lua_resume` and anything that allocates on a normal
  transition. They can run for a long time and can call back into managed code.
- From C# into Luau, give the shim a call helper per common shape. It folds six
  transitions into one and keeps the stack handling in native code.

Userdata. Use the id table.

- It was faster on both platforms: 59 ns against 119 ns, and 65 ns against 111
  ns.
- No managed code runs inside Luau's collector. With GCHandle the destructor is
  a reverse P/Invoke from the middle of whatever call triggered the collection.
- An id is the kind of value the engine already passes across its thread
  boundary. It can carry a generation, so a stale id is detectable, and a leak
  shows up as a slot count.
- The cost is that an object lives until the host next drains the queue. Once a
  frame is enough.

Scripts and environments. One Luau state, sandboxed once. One thread with
`luaL_sandboxthread` per script.

Stopping scripts. Arm the interrupt from a watchdog only when a script has
overrun. Leaving the handler installed costs 0.3 to 0.9 ns on every loop
iteration.

Memory. Do not let the allocator return null while managed code can be on the
stack. A failed allocation raises from whichever Luau call allocated, and that
includes `lua_pushlstring` called from a host function. Enforce a script memory
budget from the interrupt handler or between calls instead. This was not tried.

Native code generation. Leave it out. See check 10.

## Not measured

- `dotnet publish` on a Linux host, and Luau's CMake build on Linux. See traps
  1 and 2.
- arm64, macOS, and any console or mobile target.
- A quiet machine. The numbers carry 10 to 20 percent of noise.
- Windows with a `setjmp` that does not unwind, which would need a patch to
  Luau. It might remove most of the 1.5 us per caught error.
- `-static-libstdc++` on Linux, to drop the run-time dependency.
- A memory budget enforced from the interrupt handler.
- More than one thread touching a Luau state. Only the interrupt arm came from
  a second thread.
- `require`, the debugger callbacks, Luau's type checker, and Luau's feature
  flags, which the C API leaves at their defaults.
- Whether the unwinding failure can be made safe by any runtime setting. It was
  only shown that it fails and that the shim avoids it.

## Files

Everything is under `spikes/luau-aot/`.

| Path | What it is |
| --- | --- |
| `luau.pin` | the Luau tag |
| `build-luau.ps1`, `build-luau.sh` | fetch and build Luau, Windows and Linux |
| `run-windows.ps1`, `run-linux.ps1` | publish every variant and run the checks |
| `native/` | the shim and its CMake project |
| `cross/` | the wrappers that send the Linux link to WSL |
| `LuauSpike/` | the C# project. `Checks/` has one file per group of checks, `CrashCases.cs` the cases that run in a child process. |
| `scripts/` | the Luau scripts the checks load |
