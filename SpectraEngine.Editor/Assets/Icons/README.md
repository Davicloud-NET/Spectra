# Editor icons

One `.svg` per glyph. **The filename is the resource key**: `IconLgMove.svg`
becomes `{StaticResource IconLgMove}`, which is what the markup and the code
already say. To change an icon, replace its file and rebuild — nothing else in
the shell names a file.

`Icons.targets` turns this folder into `Theme/Icons.axaml` at build time. That
file is generated: **do not edit it**, it is overwritten.

## Adding your own

```xml
<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
     fill="none" stroke="currentColor">
  <path d="M4 12 L20 12" />
</svg>
```

- **`width`/`height` say what size the glyph renders at; `viewBox` says what
  size it was drawn at.** The build works out the ratio. So a Lucide or Feather
  icon drops in unchanged: keep its `viewBox="0 0 24 24"` and set `width="16"
  height="16"`. Aspect is preserved — a glyph whose proportions differ is
  letterboxed, never stretched.
- **Only `<path d="...">` is read.** Multiple paths in one file are
  concatenated, in document order. Circles, rects, groups, gradients and
  embedded images are ignored — convert them to paths in your editor first
  ("flatten"/"object to path"). Nothing else survives, and a file with no path
  fails the build by name rather than rendering as nothing.
- **The path data is copied verbatim.** The build never rewrites a `d` string,
  because scaling one correctly means handling arcs, relative commands and
  flags, and getting it subtly wrong produces artwork that still draws and is
  quietly deformed.
- **Sizes**: `Icon*` renders at 16, `IconLg*` at 32, and `CaretDown` is its own
  8x6. The two icon sets are separate artwork on purpose — see rule 2.

## Two rules the set is held to

**1. Every glyph's ink fills the same box.** 2.5 to 13.5 on both axes for the 16
set, 3.5 to 28.5 for the 32 set. This is not tidiness: `Path.icon` draws with
`Stretch="None"`, so the authored coordinates *are* the rendered ones, and a
glyph that fills less of its box renders smaller than its neighbours. The shell
once drew these with `Stretch="Uniform"`, which normalises each geometry to its
own ink bounds and throws the shared grid away — Move rendered at 1.33x, Resize
at 1.60x, Play at 1.78x and Stop at 2.29x, and eleven outlines at four optical
sizes are not eleven icons, they are a grey texture.

Measured today, the 32 set does **not** fully hold this: most glyphs render
26.6 across, but `IconLgBrushPart` is 30.3 tall, `IconLgLight` 24.6 and
`IconLgBrushSubtractive` 25.1. `RibbonGeometryTests.Every_large_glyph_is_drawn_to_one_optical_size`
bounds the spread loosely enough to pass what ships; a replacement set should
tighten it.

**2. Kinds are told apart by silhouette, never by interior strokes.** At 16px
with a 1.5 stroke, two shapes with the same outline and different insides are
the same shape. The three brush kinds used to be exactly that; they are now a
cube on the ground (block: fused into the level), a free cube (part), and a cube
with a hole through it (cut).

## Colour is the theme's, not the file's

Every glyph is drawn by `Path.icon` or `Path.icon-lg`, which supply `Stroke`
from the palette and `Fill="Transparent"`. That is what drives the node-kind
tints, the hover recolour, the disabled dim and the amber lit state — so
**author replacements as strokes, not fills**, in the Lucide/Feather idiom, and
put no colour in the file. `stroke="currentColor"` in the samples here is for
your editor's benefit; the build ignores it.

The one exception is the play/stop pair, which is filled because that pair is
filled everywhere in the world, and whose two glyphs are drawn to the same
optical area so the button does not change weight when a run starts.

## What is checked

- `RibbonDepthConventionTests` — every icon the markup or the code names is a
  file here, every file here is named by something, and every `IconLg*` is
  authored on the 32 box rather than the 16 one.
- `RibbonGeometryTests` — what the glyphs actually render to, measured on a real
  layout pass: centred in their box, and one optical size across the large set.
