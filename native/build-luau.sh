#!/usr/bin/env bash
# Builds Luau and its shim from the external/luau submodule, as build-luau.ps1
# does on Windows. Two files come out, and neither is committed:
#
#   native/runtimes/<rid>/native/libspectra_luau.so   for dotnet run and the test suites
#   native/runtimes/<rid>/static/libspectra_luau.a    linked into a NativeAOT publish
#
# Each holds the shim (native/luau) and the parts of Luau it needs: the VM and
# the compiler, no code generator. Errors are raised by longjmp and a vector is
# three floats.
#
# The compiler is called directly, so this needs clang++ and ar and nothing
# else. Set CXX to use another compiler. The options repeat the ones in
# native/luau/CMakeLists.txt, which the Windows build uses.
set -euo pipefail

native="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(dirname "$native")"
src="$repo/external/luau"
shim="$native/luau"
cxx="${CXX:-clang++}"

case "$(uname -m)" in
    x86_64) rid=linux-x64 ;;
    aarch64) rid=linux-arm64 ;;
    *) echo "No runtime identifier is known for $(uname -m)." >&2; exit 1 ;;
esac

if [ ! -f "$src/VM/include/lua.h" ]; then
    echo "Luau sources are missing at $src. Run: git submodule update --init --recursive" >&2
    exit 1
fi

if ! command -v "$cxx" > /dev/null; then
    echo "$cxx was not found. Install clang, or set CXX to a C++17 compiler." >&2
    exit 1
fi

release="0.$(sed -nE 's/^#define[[:space:]]+SL_LUAU_RELEASE[[:space:]]+([0-9]+).*/\1/p' "$shim/spectra_luau.h")"
if [ "$release" = "0." ]; then
    echo "native/luau/spectra_luau.h does not define SL_LUAU_RELEASE." >&2
    exit 1
fi

# A shallow checkout has no tags. The check then waits for a machine that has them.
tag="$(git -C "$src" describe --tags --exact-match 2> /dev/null || true)"
if [ -n "$tag" ] && [ "$tag" != "$release" ]; then
    echo "external/luau is at $tag and native/luau/spectra_luau.h says $release. Change SL_LUAU_RELEASE there and Lua.Release in SpectraEngine.Scripting.Luau." >&2
    exit 1
fi

# Objects go to a temporary folder. In WSL, compiling onto a Windows drive is
# several times slower.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# One set of objects serves both libraries. Only the API is visible, and the
# NativeAOT link exports nothing from an executable.
visible='__attribute__((visibility("default")))'
flags=(-std=c++17 -O3 -DNDEBUG -fPIC -fvisibility=hidden -fno-math-errno -Wall
    -I"$src/Common/include" -I"$src/Ast/include" -I"$src/Bytecode/include"
    -I"$src/Compiler/include" -I"$src/VM/include"
    -DLUA_USE_LONGJMP=1 -DLUA_VECTOR_SIZE=3
    "-DLUA_API=extern \"C\" $visible"
    "-DLUACODE_API=extern \"C\" $visible"
    "-DSL_API=extern \"C\" $visible")

export SL_CXX="$cxx" SL_FLAGS="$(printf '%q ' "${flags[@]}")"

compile() { # <object folder> <source folder>
    mkdir -p "$1"
    export SL_OBJECTS="$1"
    find "$2" -maxdepth 1 -name '*.cpp' -print0 |
        xargs -0 -P "$(nproc)" -I{} bash -c 'eval "\"$SL_CXX\" $SL_FLAGS -c \"{}\" -o \"$SL_OBJECTS/$(basename "{}" .cpp).o\""'
}

echo "Building Luau ($rid) with $("$cxx" --version | head -1)"

# A folder per part: two parts may each have a file of one name.
for part in Common Ast Bytecode Compiler VM; do
    compile "$work/$part" "$src/$part/src"
done
compile "$work/shim" "$shim"

shared="$native/runtimes/$rid/native/libspectra_luau.so"
static="$native/runtimes/$rid/static/libspectra_luau.a"
mkdir -p "$(dirname "$shared")" "$(dirname "$static")"

# --no-undefined: a part left out fails here, not when the library loads.
"$cxx" -shared -o "$shared" "$work"/*/*.o -Wl,--no-undefined -Wl,--gc-sections -Wl,--strip-debug
rm -f "$static"
ar rcs "$static" "$work"/*/*.o

echo
stat -c '%12s  %n' "$shared" "$static"
echo "Luau $release, pinned at $(git -C "$src" rev-parse HEAD 2> /dev/null || echo 'an unknown commit')"
