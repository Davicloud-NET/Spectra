#!/usr/bin/env bash
# Builds Luau for linux-x64 from the checkout that build-luau.ps1 fetched.
#
# This drives clang++ directly. The WSL distro the spike ran in has clang but
# no cmake and no make. On a machine that has cmake, native/CMakeLists.txt
# produces the same libraries.
#
# Output, next to the Windows one:
#   build/linux-x64/longjmp/lib      static libraries, errors raised with longjmp
#   build/linux-x64/cxx/lib          static libraries, errors raised with C++ exceptions
#   build/linux-x64/longjmp/shared   libluau.so, everything in one library
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
src="$root/.luau/src"
out="$root/build/linux-x64"
# Objects go to the Linux filesystem: compiling onto /mnt is several times slower.
work="${TMPDIR:-/tmp}/spectra-luau-spike"
jobs="$(nproc)"

if [ ! -f "$src/VM/include/lua.h" ]; then
    echo "Luau sources not found at $src. Run build-luau.ps1 on Windows first, it fetches them." >&2
    exit 1
fi

includes=(-I"$src/Common/include" -I"$src/Ast/include" -I"$src/Bytecode/include" -I"$src/Compiler/include"
          -I"$src/VM/include" -I"$src/VM/src" -I"$src/CodeGen/include")
# What Luau's own CMake Release build passes to clang, plus -fPIC for the shared library.
base=(-std=c++17 -O3 -DNDEBUG -fPIC -fno-math-errno -Wall -Wno-unused)

compile_dir() { # <object dir> <source dir> <flags...>
    local objdir="$1" dir="$2"
    shift 2
    mkdir -p "$objdir"
    export SPIKE_FLAGS="$(printf '%q ' "$@")" SPIKE_OBJDIR="$objdir"
    find "$dir" -maxdepth 1 -name '*.cpp' -print0 |
        xargs -0 -P "$jobs" -I{} bash -c 'eval "clang++ $SPIKE_FLAGS -c \"{}\" -o \"$SPIKE_OBJDIR/$(basename "{}" .cpp).o\""'
}

archive() { # <archive> <object dir>
    rm -f "$1"
    ar rcs "$1" "$2"/*.o
}

build_variant() { # <longjmp|cxx> <lib|shared>
    local unwind="$1" kind="$2"
    local objs="$work/$unwind-$kind" dest="$out/$unwind/$kind"
    local export_attr=""
    [ "$kind" = shared ] && export_attr=' __attribute__((visibility("default")))'

    local flags=("${base[@]}" "${includes[@]}"
        "-DLUA_API=extern \"C\"$export_attr"
        "-DLUACODE_API=extern \"C\"$export_attr"
        "-DLUACODEGEN_API=extern \"C\"$export_attr"
        "-DSL_API=extern \"C\"$export_attr")
    [ "$unwind" = longjmp ] && flags+=(-DLUA_USE_LONGJMP=1)
    [ "$kind" = shared ] && flags+=(-fvisibility=hidden)

    echo "Building Luau (linux-x64, $unwind, $kind)"
    rm -rf "$objs" "$dest"
    mkdir -p "$dest"

    local lib
    for lib in Common Ast Bytecode Compiler VM CodeGen; do
        compile_dir "$objs/$lib" "$src/$lib/src" "${flags[@]}"
    done
    mkdir -p "$objs/shim" "$objs/compile_shim"
    clang++ "${flags[@]}" -c "$root/native/spectra_luau.cpp" -o "$objs/shim/spectra_luau.o"
    clang++ "${flags[@]}" -c "$root/native/spectra_luau_compile.cpp" -o "$objs/compile_shim/spectra_luau_compile.o"

    if [ "$kind" = shared ]; then
        clang++ -shared -o "$dest/libluau.so" "$objs"/*/*.o -Wl,--gc-sections -Wl,--strip-debug
    else
        for lib in Common Ast Bytecode Compiler VM CodeGen; do
            archive "$dest/libLuau.$lib.a" "$objs/$lib"
        done
        archive "$dest/libspectra_luau_shim.a" "$objs/shim"
        archive "$dest/libspectra_luau_compile_shim.a" "$objs/compile_shim"
    fi

    (cd "$dest" && stat -c '%12s  %n' *)
}

build_variant longjmp lib
build_variant cxx lib
build_variant longjmp shared
rm -rf "$work"

echo
echo "Luau $(cat "$root/luau.pin") with $(clang++ --version | head -1)"
