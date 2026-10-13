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
    Scripts/
  Maps/
    Lobby.smap/           one level
      map.json
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

[Captions](/guides/captions/#languages) are looked up in this language. A sound with no caption in another language gets its caption in this one, and the cook compares every other language's caption file and subtitle files with this one's.

## Levels

A level is a folder whose name ends in `.smap`. The level itself is `map.json` inside it. You can read it, edit it by hand and diff it. The format is described in [Level files](/reference/level-files/).

When the editor saves, it only rewrites files that changed. Anything in a file that it doesn't understand is kept as it was.

## Assets

Textures, materials, models, sounds and scripts live under `Assets`. Everything refers to an asset by its path from that folder, with forward slashes:

```
Materials/wall.spectramat
Textures/wall_brick.png
Scripts/door.luau
```

A script is a `.luau` file under `Assets`, like any other asset. The editor puts a new one in `Assets/Scripts`, and you can keep yours in any folder there.

A project made with an older build has a `Scripts` folder beside `Assets`. Nothing reads that folder and nothing removes it. Move its files under `Assets` to use them.

The first time you open a script from the editor, four more files appear beside `Assets`: `spectra.d.luau`, `.luaurc` and two in `.vscode`. They set Visual Studio Code up for the project's scripts, and no level needs them. [Write your first script](/guides/write-your-first-script/#set-up-visual-studio-code) says what each one is for.

## Build output

`cooked` holds what [the cook](/guides/cook-and-run-a-project/) produces. It is made from the files beside it, so don't edit it and don't check it in. A new project comes with a `.gitignore` that leaves it out.
