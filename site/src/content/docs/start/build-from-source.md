---
title: Build from source
description: Get the engine, the demo and the editor running from a clone.
---

There are no downloads yet, so the way in is a clone.

## What you need

- Windows 10 or 11
- The [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git
- CMake and Visual Studio with the C++ workload, for the physics library

## Get the code

```bash
git clone --recursive https://github.com/Davicloud-NET/Spectra.git
cd Spectra
```

If you cloned without `--recursive`, fetch the physics source afterwards:

```bash
git submodule update --init --recursive
```

## Build

Physics comes from Box3D, which is native code. Build it once:

```powershell
native/build-box3d.ps1
```

Then build everything else:

```bash
dotnet build
```

## Run the demo

Pick a renderer: `opengl`, `d3d11` or `d3d12`.

```bash
dotnet run --project SpectraEngine.Executable -- d3d11
```

Hold the right mouse button to look around and use W, A, S, D to fly. Press F8 to walk the level as a character, and F8 again to come back.

## Run the editor

```bash
dotnet run --project SpectraEngine.Editor -- d3d11
```

The editor runs on `d3d11` or `d3d12`.

## Run the tests

Each suite is its own program. Use `dotnet run`, not `dotnet test`:

```bash
dotnet run --project Test/SpectraEngine.Bsp.Tests
```

The other suites sit beside it in the `Test` folder.
