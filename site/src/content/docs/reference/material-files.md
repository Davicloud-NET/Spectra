---
title: Material files
description: The .spectramat format.
---

A material is a small text file ending in `.spectramat`, kept under `Assets/Materials`. It names a shader, its textures and its numbers, and it can say what the surface is made of.

```
// Brick wall.
shader = lit
acoustic = brick

texture uDiffuse   = Textures/wall_brick.png, linearmipmap, repeat
color   uBaseColor = #B4A08C
float   uRoughness = 0.8
```

## Rules

- One setting per line.
- `//` starts a comment.
- Names are case-sensitive. They are the shader's own names.
- A line the engine can't read becomes a warning and the rest of the file still loads.

## Settings

| Line | Sets |
|---|---|
| `shader = lit` | Which shader draws the surface. `lit` is the built-in one and the default, so the line can be left out. |
| `acoustic = brick` | What the surface is made of, for sound. The line can be left out. See [What it is made of](#what-it-is-made-of). |
| `texture <name> = <path>, <options>` | A texture. The path is from the `Assets` folder. |
| `color <name> = #RRGGBB` | A colour. `#RRGGBBAA` and three or four plain numbers also work. |
| `float <name> = 0.8` | One number. |
| `vec2 <name> = 4 4` | Two numbers. `vec3` and `vec4` take three and four. |

## Texture options

Options come after the path, in any order.

| Option | Choices | Default |
|---|---|---|
| Filtering | `nearest`, `linear`, `linearmipmap` | `linearmipmap` |
| Wrapping | `repeat`, `clamp` | `repeat` |
| Contents | `srgb`, `data` | `srgb` |

Use `srgb` for a picture, such as a surface colour. Use `data` for a texture the shader does maths with, such as roughness, a normal map or a mask.

## Colours and numbers

A `color` is written the way you see it on screen, and the engine converts it for lighting. A `vec3` is passed through untouched. So use `color` for colours and `vec3` for anything else, including light that should be brighter than white.

## What the built-in shader reads

| Name | Kind | Default | Meaning |
|---|---|---|---|
| `uDiffuse` | texture | none | The surface picture. Always set one. Use a plain white texture when the colour comes from `uBaseColor` alone. |
| `uBaseColor` | color | white | Multiplied with the picture. On a metal, the colour of its reflection. |
| `uRoughness` | float | 0.65 | 0 is a mirror, 1 is chalk. |
| `uMetallic` | float | 0 | 0 for anything that isn't metal, 1 for metal. |
| `uAmbientOcclusion` | float | 1 | Lower values darken the surface in indirect light. |
| `uEmissive` | vec3 | 0 0 0 | Light the surface gives off. Values above 1 glow. |

## What it is made of

`acoustic` tells the engine what a sound has to get through when this surface is in its way. It takes one of these names, in capitals or not.

| Name | Use it for |
|---|---|
| `fabric` | Curtains, cloth and padding. Nearly everything comes through. |
| `plaster` | A light inside wall. |
| `wood` | Doors, planks and crates. |
| `glass` | Windows. A sound behind one is quieter and nearly as clear. |
| `metal` | Sheet metal: doors, lockers, ducts and containers. A sound behind it is dull. |
| `brick` | A brick wall. |
| `concrete` | Poured walls, floor slabs and bunkers. |
| `rock` | Cliffs and cave walls. The least comes through. |
| `generic` | Anything else. A material with no `acoustic` line gets this one. |

Each name makes a sound behind the surface quieter and duller by its own amount. A thicker wall of the same material lets less through.

A name that isn't in the list is a warning with the file and the line, and the material counts as having no `acoustic` line.

:::note
Nothing is heard through walls yet. The engine reads the line and keeps the answer for the sound work that comes next.
:::

## When something is missing

A material that can't be found is drawn with a magenta checker, so you can't miss it. A face with no material at all is plain grey.
