# World icons

The icons of things that stand in the world: one per entity class, one per
kind of light. One `.svg` per icon, on a 16 box, and the file name is the key.

Two readers draw every file here:

- The 3D view. The files are embedded in `SpectraEngine.Editing`.
  `ViewportGlyphs` flattens each to line segments once, and
  `EntityMarkerOverlay` and `LightOverlay` draw them facing the camera.
- The editor's shell. `SpectraEngine.Editor/Icons.targets` reads this folder
  beside its own and puts each file in `Theme/Icons.axaml`.

So a point entity looks the same in the viewport, the scene tree, the Logic
view and the menus. Never copy a file from here into the editor's folder, and
never redraw one in code.

## Which file a thing gets

`Icons/WorldIcons.cs` decides, for both readers.

| Thing | File |
|---|---|
| An entity whose class names an icon | `IconEntity` + the name in Pascal case, hyphens gone: `trigger-once` is `IconEntityTriggerOnce` |
| A class that names none, or names one with no file | `IconEntity` |
| A class this build has no schema for | `IconEntityUnknown` |
| A light | `IconLight` for a point light, `IconLightSun`, `IconLightSpot`, `IconLightPanel`, `IconLightDisc` |
| A light that is also an entity | its class's file, as any entity |

A class names its icon in `[SpectraEntity(Icon = "door")]`. The name travels in
the `.sentdef`, so a class from a game or a script names one the same way.

The colour is the entity's group: `Icons/EntityGroupTints.cs`, which repeats
the theme's `SpectraGroup` tokens. A light is drawn in its own colour.

## Where the files come from

They are [Tabler Icons](https://tabler.io/icons), MIT. The licence is
`LICENSE-tabler.txt`. `SpectraEngine.Editor/Assets/Icons/tabler.py` writes
them: its `WORLD` table says which Tabler icon each key is. That script is the
only way a Tabler glyph gets in.

## Rules

- `width`, `height` and the `viewBox` are all 16, starting at 0 0.
- Only `<path d="...">` is read, by both readers. Convert circles and rects to
  paths.
- Every path starts with a move. A path that opens with a relative `m` starts
  from 0 0 in both readers. The build stops on a path that opens with
  anything else.
- A path too short to see is a dot. The 3D view draws it as a short dash.

## Adding one

1. Add a line to `WORLD` in `tabler.py` and run it.
2. Name it from the class: `[SpectraEntity("my_class", Icon = "my-icon")]` for
   the file `IconEntityMyIcon.svg`.

## Swapping one

Change its line in `WORLD` and run the script, or replace the file by hand and
change the line to match. Nothing else names a Tabler icon, and no test knows
what a file draws.

`IconDriftConventionTests` fails if a file here is used by no class, light or
fallback, if a built-in class names an icon with no file, or if the script's
table and a file disagree. `ViewportGlyphTests` fails if a file is not embedded
or cannot be drawn.
