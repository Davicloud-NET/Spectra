# Editor icons

One `.svg` per glyph. The file name is the resource key: `IconMove.svg` becomes
`{StaticResource IconMove}`. `Icons.targets` turns two folders into
`Theme/Icons.axaml` at build time. That file is generated, so don't edit it.

| Folder | Holds |
|---|---|
| this one | the shell's own glyphs: tools, verbs, panels, files |
| `SpectraEngine.Editing/Assets/Icons` | things that stand in the world: `IconEntity*` and `IconLight*` |

The second folder is not here because the 3D view draws those files too, and
`SpectraEngine.Editing` may not reference the editor. It has its own README.
A key is in one folder or the other, never both: the build stops on a
duplicate.

Most glyphs are [Tabler Icons](https://tabler.io/icons), MIT. The licence is
`LICENSE-tabler.txt`, in both folders. `tabler.py` writes those files to both
folders, and the three tables at its top say which Tabler icon each key is.

## Two sets

| Files | Box | Drawn by |
|---|---|---|
| `Icon*` | 16 | `Path.icon`, everywhere |
| `IconLg*` | 24 | `Path.icon-lg`, on the ribbon's large buttons |

Tabler draws on 24. The small set is the same paths scaled to 16 by
`tabler.py`.

An entity or a light has no `IconLg` file. A large button that shows one asks
`EditorIcons.LargeGlyph`, which scales the world's file up, so the two sizes
cannot be two drawings. A test holds every other large file to the Tabler icon
its small twin is.

A few are drawn by hand and are not in the script: `CaretDown`, `IconCheckBox`,
`IconCheckTick`, `IconRadio`, `IconRadioDot`, `IconPlay`, `IconStop` and
`IconEmpty`.

## Rules

- `width` and `height` equal the `viewBox`. A file where they differ becomes a
  geometry with a transform, and Avalonia throws when a `Path` with a `Stretch`
  other than `None` draws one. That is why the small set is scaled in the file.
- Only `<path d="...">` is read. Several paths in one file are joined. Circles,
  rects and groups are ignored, so convert them to paths first.
- Every path starts with a move, `M` or `m`. The build stops on one that does
  not.
- Glyphs are stroked. The styles set the stroke width and colour. Play and Stop
  are the two that are filled.
- The icon of an entity, a light or a file is named in one place,
  `Shell/EditorIcons.cs`. The scene tree, the Logic view, the entity lists, the
  Content panel and the drop prompt all ask it. Markup that needs one uses
  `{x:Static shell:EditorIcons...}`.

## Adding one

1. Add a line to `SMALL` in `tabler.py`, and to `LARGE` if the ribbon needs it
   big. Run `python tabler.py` in this folder.
2. Name it in the markup: `<Path Classes="icon" Data="{StaticResource IconName}" />`.

A file kind's icon is `IconFile*`: add the line to `SMALL` and the kind to
`EditorIcons.ForFile`.

## Swapping one

Change its line in `tabler.py` and run the script, or replace the file by hand
and change the line to match. Nothing else names a Tabler icon.

`ShellLookConventionTests` fails if the markup names a glyph with no file, if a
file here is named by nothing, or if a file is on the wrong box.
`IconDriftConventionTests` fails if a table and a file disagree, or if a source
outside `EditorIcons.cs` names an entity, light or file icon.
