# Editor icons

One `.svg` per glyph. The file name is the resource key: `IconMove.svg` becomes
`{StaticResource IconMove}`. `Icons.targets` turns this folder into
`Theme/Icons.axaml` at build time. That file is generated, so don't edit it.

Most glyphs are [Tabler Icons](https://tabler.io/icons), MIT. The licence is
`LICENSE-tabler.txt`. `tabler.py` writes those files; the two tables at its top
say which Tabler icon each key is.

## Two sets

| Files | Box | Drawn by |
|---|---|---|
| `Icon*` | 16 | `Path.icon`, everywhere |
| `IconLg*` | 24 | `Path.icon-lg`, on the ribbon's large buttons |

Tabler draws on 24. The small set is the same paths scaled to 16 by
`tabler.py`.

A few are drawn by hand and are not in the script: `CaretDown`, `IconCheckBox`,
`IconCheckTick`, `IconRadio`, `IconRadioDot`, `IconPlay`, `IconStop`,
`IconEmpty`, `IconEntityMover`, `IconEntityLogic`, `IconEntityTrigger` and
`IconEntityPlayer`.

Those last four stand for an entity group on a Logic view card: movers, logic,
triggers and the player. One per group, never one per class: the shell knows no
class by name. They are drawn in the stroke style of the Tabler set, on the
same 16 box, and none is copied from it.

## Rules

- `width` and `height` equal the `viewBox`. A file where they differ becomes a
  geometry with a transform, and Avalonia throws when a `Path` with a `Stretch`
  other than `None` draws one. That is why the small set is scaled in the file.
- Only `<path d="...">` is read. Several paths in one file are joined. Circles,
  rects and groups are ignored, so convert them to paths first.
- Glyphs are stroked. The styles set the stroke width and colour. Play and Stop
  are the two that are filled.

## Adding one

1. Add a line to `SMALL` in `tabler.py`, and to `LARGE` if the ribbon needs it
   big. Run `python tabler.py` in this folder.
2. Name it in the markup: `<Path Classes="icon" Data="{StaticResource IconName}" />`.

`ShellLookConventionTests` fails if the markup names a glyph with no file, if a
file is named by nothing, or if a file is on the wrong box.
