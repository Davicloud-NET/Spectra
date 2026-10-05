#!/usr/bin/env bash
# Runs a Linux tool with arguments that MSBuild wrote for Windows.
# Usage: wsl-run.sh <tool> <file holding the argument line>
set -euo pipefail

tool="$1"
line="$(tr -d '\r' < "$2")"

# D:\a\b becomes /mnt/d/a/b. Nothing else in a link line has a backslash.
line="$(printf '%s' "$line" | sed -E 's#\\#/#g; s#([A-Za-z]):/#/mnt/\L\1/#g')"

eval "set -- $line"
exec "$tool" "$@"
