---
title: Level files
description: What is inside a .smap folder.
---

A level is a folder ending in `.smap`. The level is the `map.json` file inside it.

The file is plain JSON, written the same way every time, so a saved level only differs where you changed it. Members the engine doesn't know are kept when it saves. Values that are at their default are left out.

## The top

```json
{
  "spectramap": 4,
  "minimumReadableVersion": 1,
  "engine": "1.0.0",
  "scene": {
    "name": "Lobby"
  },
  "nodes": []
}
```

`spectramap` is the format version. `nodes` holds the top-level nodes, each with its children inside it.

## A node

```json
{
  "id": "2dbe4ebc-44ec-46d5-a2dc-6d2710901e8f",
  "name": "PillarA",
  "transform": {"p":[-2,0.1,-2]},
  "children": []
}
```

| Member | Holds |
|---|---|
| `id` | The node's id. Never changes. |
| `name` | The node's name. |
| `transform` | `p` is the position. `r` is the rotation as a quaternion `[x, y, z, w]`. `s` is the scale. |
| `kind` | `"part"` for a part. Left out for a block or a cut. |
| `collide` | `false` when the node is not solid. |
| `query` | `false` when rays and overlap checks skip the node. |
| `touch` | `false` when the node raises no touch events. |
| `render` | `false` when the node is not drawn. |
| `brush`, `mesh`, `light`, `entity` | What the node carries, if anything. |
| `children` | The nodes inside this one. |

## A brush

```json
"brush": {
  "planes": [
    [1,0,0,-0.2],
    [-1,0,0,-0.2],
    [0,1,0,-1.1],
    [0,-1,0,-1.1],
    [0,0,1,-0.2],
    [0,0,-1,-0.2]
  ],
  "faces": [
    {"material":"Materials/checker_orange.spectramat"},
    {"material":"Materials/checker_orange.spectramat"},
    {"material":"Materials/checker_orange.spectramat"},
    {"material":"Materials/checker_orange.spectramat"},
    {"material":"Materials/checker_orange.spectramat"},
    {"material":"Materials/checker_orange.spectramat"}
  ]
}
```

A brush is the space behind a set of planes. Each plane is `[x, y, z, d]`: the direction the face points, and minus its distance from the brush's origin. The example is a pillar 0.4 wide and 2.2 tall.

`faces` has one entry per plane, in the same order.

| Member | Holds |
|---|---|
| `operation` | `"subtractive"` for a cut. Left out otherwise. |
| `planes` | The faces, as above. |
| `faces` | Each face's `material`, and how its texture is laid out: `u` and `v` axes, `uo` and `vo` offsets, `us` and `vs` scales. |

## A mesh

```json
"mesh": {"model":"Models/crate.obj","submesh":1}
```

The model's path, and which piece of it this node draws when the model has several.

## A light

```json
"light": {"kind":"point","color":[1,0.26,0.03],"intensity":45,"range":9}
```

| Member | Holds |
|---|---|
| `kind` | `"point"`, `"spot"`, `"rect"` or `"disc"`. Left out for a sun. |
| `color` | Red, green and blue, in linear light rather than as a screen colour. |
| `intensity` | How bright. |
| `range` | How far it reaches. |
| `innerAngle`, `outerAngle` | A spot light's cone, as half-angles in degrees. |
| `width`, `height`, `radius` | An area light's size. |
| `enabled` | `false` when the light is switched off. |

## An entity

```json
"entity": {
  "class": "math_counter",
  "keys": {"startvalue":"0","min":"0","max":"6"},
  "outputs": [
    {"output":"OnHitMax","target":"Gate","input":"Disable","delay":0.1,"times":1}
  ]
}
```

`keys` are the entity's settings, always as text. `outputs` are its wires. See [Entities and wiring](/concepts/entities-and-wiring/).
