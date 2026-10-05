#!/usr/bin/env bash
# Copies the builds to the Linux filesystem and runs them there.
# Usage: run-in-wsl.sh <the spike's out folder, as a /mnt path> <runs>
set -uo pipefail

out="$1"
runs="${2:-3}"
work="$HOME/spectra-openal-spike"
logs="$out/linux-x64/logs"
export DOTNET_ROOT="$HOME/spectra-dotnet10"

rm -rf "$work"
mkdir -p "$work" "$logs"
rm -f "$logs"/*.txt

cp -r "$out/jit" "$work/jit"
for n in $(seq 1 "$runs"); do
    "$DOTNET_ROOT/dotnet" "$work/jit/OpenAlFilterSpike.dll" > "$logs/jit-$n.txt"
    echo "jit run $n: exit code $?"
done

if [ -f "$out/linux-x64/aot/OpenAlFilterSpike" ]; then
    cp -r "$out/linux-x64/aot" "$work/aot"
    chmod +x "$work/aot/OpenAlFilterSpike"
    for n in $(seq 1 "$runs"); do
        "$work/aot/OpenAlFilterSpike" > "$logs/aot-$n.txt"
        echo "aot run $n: exit code $?"
    done
    echo "== what the AOT executable links to"
    ldd "$work/aot/OpenAlFilterSpike"
fi

echo "== what the packaged libopenal.so links to"
ldd "$work/jit/runtimes/linux-x64/native/libopenal.so"

rm -rf "$work"
