"""Writes the icon files that come from Tabler Icons (MIT).

Run from this folder: python tabler.py

Tabler draws on a 24 box. The large set (IconLg*) keeps it. The small set
(Icon*) is the same paths scaled to a 16 box, so no file needs a transform:
Avalonia throws when a stretched Path draws a geometry that carries one.

The bounding path Tabler ships in every file is dropped. It has no stroke in
the original, but the build strokes every path it finds.
"""
import os
import re
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = "https://raw.githubusercontent.com/tabler/tabler-icons/main/icons/outline/{}.svg"

SMALL = {
    "IconArrowUp": "arrow-up",
    "IconBrushPart": "box",
    "IconBrushSubtractive": "cube-off",
    "IconBrushWorld": "cube",
    "IconChevronRight": "chevron-right",
    "IconClear": "x",
    "IconConvertKind": "switch-horizontal",
    "IconDelete": "trash",
    "IconDuplicate": "copy",
    "IconEntity": "diamond",
    "IconFrame": "focus-centered",
    "IconFrameAll": "maximize",
    "IconGrid": "layout-grid",
    "IconGroup": "folder",
    "IconGroupNodes": "box-multiple",
    "IconLight": "bulb",
    "IconList": "list",
    "IconMap": "map",
    "IconMesh": "vector-triangle",
    "IconMove": "arrows-move",
    "IconOpenFolder": "folder-open",
    "IconRedo": "arrow-forward-up",
    "IconRefresh": "refresh",
    "IconResize": "resize",
    "IconRotate": "rotate-clockwise",
    "IconSurfaceLight": "lamp",
    "IconUndo": "arrow-back-up",
    "IconUngroupNodes": "stack-2",
    "IconWarning": "alert-triangle",
}
LARGE = {
    "IconLgBrushPart": "box",
    "IconLgBrushSubtractive": "cube-off",
    "IconLgBrushWorld": "cube",
    "IconLgDuplicate": "copy",
    "IconLgEntity": "diamond",
    "IconLgFrame": "focus-centered",
    "IconLgFrameAll": "maximize",
    "IconLgLight": "bulb",
    "IconLgMap": "map",
    "IconLgMove": "arrows-move",
    "IconLgNewProject": "folder-plus",
    "IconLgOpenFolder": "folder-open",
    "IconLgResize": "resize",
    "IconLgRotate": "rotate-clockwise",
}

ARGUMENTS = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7}
NUMBER = re.compile(r"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")
BOUNDS = "M0 0h24v24H0z"


def commands(d):
    """Path data as (command, values). An arc's two flags are single characters."""
    at, command, values = 0, None, None
    result = []
    while at < len(d):
        ch = d[at]
        if ch in " ,\t\r\n":
            at += 1
        elif ch.isalpha():
            command, values = ch, []
            result.append((command, values))
            at += 1
        elif command.upper() == "A" and len(values) % 7 in (3, 4):
            if ch not in "01":
                raise ValueError(f"arc flag expected at {at} in {d}")
            values.append(ch)
            at += 1
        else:
            match = NUMBER.match(d, at)
            if not match:
                raise ValueError(f"number expected at {at} in {d}")
            values.append(float(match.group()))
            at = match.end()

    for command, values in result:
        if command.upper() != "Z" and (not values or len(values) % ARGUMENTS[command.upper()]):
            raise ValueError(f"{command} with {len(values)} values in {d}")
    return result


def text(value):
    rounded = f"{round(value, 3):.3f}".rstrip("0").rstrip(".")
    return "0" if rounded in ("-0", "") else rounded


def scaled(d, factor):
    """A uniform scale: every length, but not an arc's rotation or its flags."""
    parts = []
    for command, values in commands(d):
        parts.append(command)
        for index, value in enumerate(values):
            if isinstance(value, str):
                parts.append(value)
            elif command.upper() == "A" and index % 7 == 2:
                parts.append(text(value))
            else:
                parts.append(text(value * factor))
    return " ".join(parts)


def write(key, name, size):
    with urllib.request.urlopen(SOURCE.format(name)) as response:
        svg = response.read().decode("utf-8")

    paths = [d for d in re.findall(r'<path[^>]*\bd="([^"]+)"', svg) if d != BOUNDS]
    if not paths:
        raise ValueError(f"{name} has no path")

    note = f'Tabler Icons "{name}", MIT. See LICENSE-tabler.txt.'
    if size != 24:
        paths = [scaled(d, size / 24) for d in paths]
        note = f'Tabler Icons "{name}", MIT, scaled from its 24 box to {size}. See LICENSE-tabler.txt.'

    body = "\n".join(f'  <path d="{d}" />' for d in paths)
    content = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        f"<!-- {note} -->\n"
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}"\n'
        '     fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round">\n'
        f"{body}\n"
        "</svg>\n"
    )
    with open(os.path.join(HERE, key + ".svg"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)


def main():
    for key, name in SMALL.items():
        write(key, name, 16)
    for key, name in LARGE.items():
        write(key, name, 24)
    print(f"{len(SMALL)} small, {len(LARGE)} large")


if __name__ == "__main__":
    main()
