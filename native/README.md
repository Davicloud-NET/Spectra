# Native dependencies

## Box3D

The physics solver, vendored as a **git submodule** at `external/box3d` and
pinned to an exact commit. Upstream is [erincatto/box3d](https://github.com/erincatto/box3d),
MIT licensed.

```bash
git submodule update --init --recursive
```

```powershell
native/build-box3d.ps1                  # win-x64, Release
native/build-box3d.ps1 -Rid win-arm64
native/build-box3d.ps1 -Clean
```

Output lands in `native/runtimes/<rid>/native/box3d.dll`. That directory is
build output and is **not** committed — the submodule commit is the thing under
version control, and a binary that can drift from it is worse than no binary.

### No Developer Command Prompt is needed, and none should be used

CMake's Visual Studio generator locates MSVC through the registry and drives
MSBuild itself, so `cl` on `PATH` is irrelevant to this build. *(This is a
different path from the NativeAOT publish, which shells out to the ilcompiler's
`findvcvarsall.bat` and does care — see the note in `CLAUDE.md`.)*

Verified on this machine: CMake 4.2.3 → Visual Studio 18 2026 generator → MSVC
14.50.35717, from an ordinary shell.

### Why a pinned commit and not the tag

`v0.1.0` is the **only** tag upstream has ever cut, and `main` has moved well
past it — the pinned commit reports version **0.2.0**. Tracking a tag would mean
tracking a year-old alpha; tracking `main` would mean the build changing under
us without a commit in this repo saying so. A submodule pin is neither: the SHA
is recorded here, and moving it is a reviewable change.

### Build options are a contract, not preferences

`build-box3d.ps1` is the only place they live, so a developer build, a CI build
and a release build cannot drift into producing different physics. Two of them
are decisions rather than tuning:

| Option | Value | Why |
| --- | --- | --- |
| `BOX3D_DOUBLE_PRECISION` | `OFF` | The float build, decided 2026-08-21 (`docs/physics.md` §7 item 2). **ABI-affecting** — the two modes ship as mutually exclusive builds, so flipping this invalidates every bound struct layout, not just performance. |
| `BUILD_SHARED_LIBS` | `ON` | A DLL beside the managed assembly, which is what `[LibraryImport]` resolves and what a NativeAOT publish carries. |

The rest (`BOX3D_SAMPLES`, `BOX3D_UNIT_TESTS`, `BOX3D_BENCHMARKS`, `BOX3D_DOCS`)
are off because we do not consume them: the samples pull a windowing stack this
engine already has, and the unit tests are upstream's and run upstream.

### Units

`b3SetLengthUnitsPerMeter(1.0f)` at startup, because **one spectraunit is one
metre**. Box3D scales its own collision and constraint tolerance macros by that
value, so the solver runs at exactly the scale it was tuned for and nothing
converts at the binding boundary.

**Two corrections to an earlier version of this note, both from reading the
sources rather than the docs:**

1. **The call must precede the first `b3Default*Def()`, not merely the first
   world.** Those constructors bake the scale in *at call time* —
   `b3DefaultWorldDef` computes `contactSpeed = 3.0f * lengthUnits` and
   `maximumLinearSpeed = 400.0f * lengthUnits`, `b3DefaultBodyDef` computes
   `sleepThreshold = 0.05f * lengthUnits`, `b3DefaultShapeDef` computes
   `density = 1000.0f / lengthUnits³`. Setting the unit after taking a default
   def gives a def tuned for the old scale.
2. **Density is not "a quantity that does not rescale" — it rescales by the
   inverse cube.** It reads as 1000 only because our unit happens to be 1 m.
   Gravity genuinely does not rescale (Box3D's default is −10 on Y and it
   declares no up axis), which is why `PhysicsDefaults` states it.

Anything whose unit is not a pure length still belongs in
`SpectraEngine.Core/Physics/PhysicsDefaults.cs` rather than being inherited.

### The ABI guard

```powershell
native/build-box3d.ps1 -Abi        # rebuild box3d.dll AND regenerate the manifest
```

`native/abi-probe` is a small C program compiled against the **same headers and
the same precision flag** as `box3d.dll`. It prints the real `sizeof`,
`_Alignof` and `offsetof` of every struct the managed binding mirrors;
`native/box3d-abi.manifest` is that output, **committed**.

`Box3DAbiTests` then checks every managed struct against it, field by field.
That test needs no C toolchain and no native library — it reads the committed
text — so every developer and every CI job gets the check for free, and
regenerating the manifest is a deliberate act when the pin moves, with the
layout diff landing in review.

**Why it matters more here than usual:** a P/Invoke struct whose layout
disagrees with the library's does not fail to compile and does not throw. It
silently reads and writes the wrong bytes. Box3D is alpha and its API is already
breaking — recent upstream commits bump `b3HullData` and `b3CompoundData`
versions — so "the pin moved and a struct grew a field" *will* happen; the only
question is whether it surfaces as a red build or as physics that is subtly
wrong and irreproducible.

The guard has been watched to fail, which is the only way to know a guard works:
swapping `b3Quat`'s scalar and vector fields — a mistake that leaves `sizeof`
**identical at 16 bytes** and produces rotations that look almost right — is
caught on field offsets. A size-only check would have missed it entirely.

**Still owed:** a CI job that regenerates the manifest and fails on a diff, so
upstream drift is caught even when nobody re-runs the probe locally.

### Do not take a third-party C# binding as a dependency

Both existing ones are prior art to read, not packages to install: one is
ClangSharp autogen on a *daily* workflow tracking alpha `main`, the other a
recent single-author package. This project has already been burned twice by
that class of dependency — a recommended-then-archived Luau binding, and a
networking library that published clean under NativeAOT and then crashed the
native binary on its default API through a reflective assembly scan.

## Luau

The script VM, a git submodule at `external/luau` pinned to release 0.742.
Upstream is [luau-lang/luau](https://github.com/luau-lang/luau), MIT licensed.

```powershell
native/build-luau.ps1                   # win-x64
native/build-luau.ps1 -Rid win-arm64
native/build-luau.ps1 -Clean
```

```bash
native/build-luau.sh                    # the host it runs on, linux-x64 or linux-arm64
```

Two files come out. Neither is committed.

| File | For |
| --- | --- |
| `native/runtimes/<rid>/native/spectra_luau.dll`, `libspectra_luau.so` | `dotnet run` and the test suites, which run under the JIT |
| `native/runtimes/<rid>/static/spectra_luau.lib`, `libspectra_luau.a` | a NativeAOT publish, which links it into the executable |

Each holds the shim in `native/luau` and the parts of Luau it needs: the VM
and the compiler.

On Windows the script needs CMake and MSVC and no Developer prompt, as for
Box3D. On Linux it calls `clang++` and `ar` and nothing else. It does not use
CMake there, so the options are written down twice: in
`native/luau/CMakeLists.txt` and in `build-luau.sh`.

### The shim is the only way in

Luau raises an error with `longjmp`, to the nearest protected call. When that
jump crosses a managed frame, the .NET runtime keeps a record of a stack that
is gone. Under the JIT nothing happens. A NativeAOT build dies at a later
garbage collection, or goes on with damaged objects.

So managed code never calls anything in Luau that can raise.
`native/luau/spectra_luau.cpp` owns:

- `callHost` and `callHostById`, the two functions Luau sees for every managed
  function. The first calls a function pointer. The second calls the
  binding's one entry point with the host's handle and a number, which is how
  a delegate is reached. The managed function returns a count, `-1` or `-2`
  for an error with the value on top, or `-3 - n` to yield `n` values. The
  raise or the yield happens after it has returned. With `-1` a message gets
  the position of the calling script in front. With `-2` the value is raised
  as it is, for a host function that passes on the error of a script it called.
- every call that can raise, in a form that returns a status: load, call,
  resume, cancel, read and write a field, push a string, make a table, a
  thread or userdata, keep a value by a reference, turn a value into text,
  open the libraries, seal the state. Most run under `lua_pcall`. A push can
  raise too, once one frame holds 8000 values, so even a number is pushed
  through a check. A refused call puts its message past that limit and takes
  none of the caller's values.
- the same forms on a thread that has yielded or ended in an error: they
  refuse. A protected call there moves the thread's stack base, and Luau only
  checks for it in a debug build. What needs no protected call still works
  there: nil, a boolean, a number, a vector, a copy, a kept value, a read
  from a table without metamethods, and `sl_xmove`. So a string for a thread
  that waits is made on the main thread and moved, and `sl_resume` does the
  moving: it takes its arguments from another thread's stack.
- what a thread returned. A thread that ended with values on its stack reads
  as one that has not started, and a second resume would call its first
  result. `sl_resume` moves the values to a thread of the shim's own, which
  `sl_results` hands out, and leaves the ended thread empty.
- the allocator. It counts and has no limit, because a refused allocation is a
  raise from whichever call allocated.
- the calls into Luau, counted. The outermost one gets a serial, and
  `sl_callstate` reports the serial and whether the call still runs. A
  watchdog on another thread reads that and times the call by itself, so the
  thread that runs scripts reads no clock.
- the interrupt. `sl_interrupt_arm` names a serial, and the handler stops the
  script when that call is the one running. A request that comes late names a
  call that has ended and does nothing. The handler also holds the memory
  budget: over it, it runs a full collection, and when the state still holds
  too much it stops the script. Either way the script gets an error at the
  line it was on, and again at every loop and call until the call ends or the
  host asks why with `sl_takestop`. A pcall in the script cannot hold on.
- the userdata of managed objects. It holds a slot and a generation of the
  host's table, never a pointer. One userdata exists per slot at a time, kept
  in a table with weak values, so two reads of one object are equal in a
  script. The collector's destructor may not touch the VM. It only notes
  which slot and generation went, and `sl_takereleased` hands the notes over.
- the metatable of vectors. Luau keeps one for the whole type, and its vector
  library sets one that knows `x`, `y` and `z` and nothing else.
  `sl_setvectormetatable` puts the host's table in its place and freezes it.
  That is how a script's `Vector3` gets `Magnitude` and its methods.
- the compiler's names for a vector. `sl_compile` takes the name of a library
  and of its constructor, and the binding passes `Vector3` and `new`. The
  compiler then makes `Vector3.new(1, 2, 3)` a vector with no call, and takes
  `Vector3` for a name of the vector type.
- a thread's data. The binding keeps a script's number in the low half and a
  thread's slot in the high half. A thread that a script makes gets the low
  half of its maker's, so a coroutine belongs to its script.
- the sealing. `luaL_sandbox` freezes the tables that are globals. The shim
  first freezes every table inside them, and a userdata metatable the same way.
- `sl_frame`, which reads one frame of a call stack. A thread that ended in an
  error keeps its frames, and that is where a traceback comes from.
- the panic handler. It keeps its message in `sl_fatal_message` for a debugger
  and prints nothing.
- Luau's feature flags, set the way Luau's own tools set them, but for one.
  `LuauCoroutineFinally` stays off, so a script has no `coroutine.finally`.
  Luau runs a coroutine's finalizers by recursion on the native stack, and its
  limit of 200 C calls does not count that. 20000 of them on one coroutine
  overflow the stack, which ends the process with no error to catch. A host
  that ends a waiting thread runs them the same way.
- `sl_constant`, which reports every number the binding copied from the
  headers, and the shim's revision. `LuauLibrary.FindProblem` compares them
  before a host opens a state, so a library left over from before a change to
  the shim is named.

`Test/SpectraEngine.Scripting.CrashCases` is a console that shows the rule
holding and what happens without it. One case raises from a managed frame on
purpose. The suite fails when that case gets through, because the other cases
then prove nothing. Two cases go through the binding: one ends scripts by an
error, by an exception in a host function, by the watchdog and by the memory
budget, and one yields from a host function, all with the collector at work.
A third piles finalizers on a coroutine, four ways. It dies of a stack
overflow when `coroutine.finally` is there, also under the JIT, and is a case
because a case has a process to itself.
The console only means something as a native executable:

```powershell
dotnet publish Test/SpectraEngine.Scripting.CrashCases -c Release -r win-x64
dotnet run --project Test/SpectraEngine.Scripting.Tests
```

### How an executable gets it

It imports `native/Luau.targets`. The demo, the editor and `scook` do.

- A build copies the shared library beside the output when it has been built,
  and says nothing when it has not.
- A NativeAOT publish links the static library, and links again when the
  library is newer than the executable. When it is missing the publish stops
  with one line that names the script to run.
- On Linux the published executable then needs `libstdc++.so.6` and
  `libgcc_s.so.1` to start, because Luau is C++.

### Build options

| Option | Value | Why |
| --- | --- | --- |
| `LUA_USE_LONGJMP` | 1 | A caught error costs less than with C++ exceptions, and an error nobody catches reaches the panic handler with its message. `docs/spikes/2026-10-luau-aot.md` has the numbers, taken on release 0.741. |
| `LUA_VECTOR_SIZE` | 3 | A vector is three floats. It is Luau's default, set anyway so that a change is made on purpose. |
| C runtime | `/MT` | The one a NativeAOT executable links. With `/MD` the publish fails with LNK2038. |
| Code generator | left out | It helps arithmetic and little else, and it makes executable memory at run time. |

The shim refuses to compile with another unwind mode or vector size, and the
binding checks both at startup.

### Changing the shim

1. Raise `SL_SHIM_REVISION` in `native/luau/spectra_luau.h` and
   `Shim.Revision` in `SpectraEngine.Scripting.Luau/Native/Shim.cs`.
2. Run the build script again. Until then the startup check refuses the old
   library by its revision.
3. Publish the crash cases again and run `SpectraEngine.Scripting.Tests`.

### Moving the pin

1. Check out the new release tag in `external/luau`.
2. Change `SL_LUAU_RELEASE` in `native/luau/spectra_luau.h` and `Lua.Release`
   in `SpectraEngine.Scripting.Luau/Native/Lua.cs`. The build scripts compare
   the first with the submodule's tag when the checkout has tags. CI has none
   and asks Luau's repository which commit the release is.
3. Build, and run `SpectraEngine.Scripting.Tests`. A constant that moved shows
   up there by name.
4. Read the calls listed in `BindingSourceTests` again in the new `lapi.cpp`.
   The list says they cannot raise, and only reading the source can confirm it.
5. Look for `LuauCoroutineFinally` there too. Once Luau drops the flag,
   `coroutine.finally` is in every build and `staysOff` in the shim does
   nothing. A test in `ScriptRunTests` then fails, and the case
   `many-finalizers` says whether the finalizers still overflow the stack.
6. Publish the crash cases and run the suite once more.

### Not proven

- `win-arm64` and `linux-arm64`. The scripts accept them and nobody has run them.
- A publish on a Linux machine. The Linux executable that was tested was
  compiled on Windows and linked in WSL, also from a path with a space in it.
- The panic handler, the allocator's abort and a compiler that returns null.
  Each ends the process or needs memory to run out, so no test runs them.
- The memory budget against one call that allocates far past it. The check
  runs at a script's loops and calls, so `string.rep` with a huge count is
  over the budget before anything looks. The script is stopped right after.
- How deep host functions and scripts may nest on a small stack. One level
  takes about 2 KB of native stack on Windows, and Luau refuses the 200th.
