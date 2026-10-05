---
title: Projects and levels
description: What a project looks like on disk.
---

A project is a folder. Everything you make is an ordinary file inside it, and most of it is text, so it works with git.

```
MyGame/
  MyGame.spectraproj      the project file
  Assets/
    Textures/
    Materials/
    Models/
  Maps/
    Lobby.smap/           one level
      map.json
  Scripts/
  cooked/                 build output
```

## The project file

`MyGame.spectraproj` names the project, lists its levels and says which one starts first.

```json
{
  "spectraproject": 1,
  "minimumReadableVersion": 1,
  "engine": "1.0.0",
  "name": "MyGame",
  "id": "dfda7d2c-68f6-4164-b984-3dabe4e01001",
  "startupMap": "Maps/Lobby.smap",
  "maps": [
    "Maps/Lobby.smap"
  ],
  "display": {"width":1280,"height":720,"vsync":true,"mode":"windowed"}
}
```

### Language

Add `language` after `id` to say which language the project's own text is written in:

```json
  "id": "dfda7d2c-68f6-4164-b984-3dabe4e01001",
  "language": "de",
```

The value is a short lowercase tag such as `en`, `de` or `pt-br`. A project that names none is in `en`. A value that is not such a tag stops the project from opening.

[Captions](/guides/captions/#languages) are looked up in this language. A sound with no caption in another language gets its caption in this one, and the cook compares every other language's caption file with this one's.

## Levels

A level is a folder whose name ends in `.smap`. The level itself is `map.json` inside it. You can read it, edit it by hand and diff it. The format is described in [Level files](/reference/level-files/).

When the editor saves, it only rewrites files that changed. Anything in a file that it doesn't understand is kept as it was.

## Assets

Textures, materials and models live under `Assets`. Everything refers to an asset by its path from that folder, with forward slashes:

```
Materials/wall.spectramat
Textures/wall_brick.png
```

## Build output

`cooked` holds what [the cook](/guides/cook-and-run-a-project/) produces. It is made from the files beside it, so don't edit it and don't check it in. A new project comes with a `.gitignore` that leaves it out.
