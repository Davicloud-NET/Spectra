---
title: scook
description: The cook tool's commands and options.
---

`scook` turns a project into a pack. From the repository it runs as:

```bash
dotnet run --project Spectra.Kitchen.CLI -- <command> [options] <path>
```

## Commands

| Command | Does |
|---|---|
| `cook <projectDir>` | Cooks the project into a pack. This is the default, so `cook` can be left out. |
| `verify <pack>` | Checks a pack: every entry decodes and everything it refers to is inside. |
| `inspect <pack>` | Lists the pack's header and entries. Add `--json` for JSON. |
| `clean <projectDir>` | Deletes the project's cook output and cook cache. |
| `sounds <contentDir> -o <dir>` | Cooks every `.wav` under a folder into `.saudio` files under `<dir>`, with no project and no pack. Only a sound whose `.wav` or `.markers.txt` changed is cooked again, and the cooked file of a `.wav` that is gone is removed. Each cooked file has a `.stamp` file beside it that records what it was cooked from. The demo's build uses it. |

## Options

| Option | Does |
|---|---|
| `-o`, `--output <path>` | Where the output goes. The default is the project's `cooked` folder. |
| `-t`, `--target <backend>` | Which renderers shaders are cooked for, separated by commas: `opengl`, `d3d11`, `d3d12`, `all`. The default is all three. |
| `-j`, `--jobs <n>` | How many workers to use. |
| `--no-cache` | Cooks everything again and leaves the cache alone. |
| `--loose` | Writes a folder of cooked files instead of one pack. |
| `--keep-brush-source` | Keeps every brush's shape in a cooked level, not only the parts'. |
| `--strict` | Treats warnings as errors. |
| `--manifest <path>` | Writes a JSON list of every asset, its inputs and the hash of its output. |
| `-q`, `--quiet` | Prints errors only. |
| `--no-color` | Plain text output. `NO_COLOR` works too. |

`--profile`, `--script-source` and `--encoder` are accepted but do nothing yet.

## The cache

Cooking keeps a cache in `.spectra-cook` inside the project, so a second cook only redoes what changed. The cache never changes the result: a cook from the cache gives the same bytes as a clean one.

## Messages

Problems are printed in a form editors can jump to:

```
<file>(<line>,<col>): error SC5001: <message>
```

The first digit of the code says what kind of asset it is about:

| Code | About |
|---|---|
| `SC0xxx` | the project or the command line |
| `SC1xxx` | finding files |
| `SC2xxx` | images |
| `SC3xxx` | models |
| `SC4xxx` | audio |
| `SC5xxx` | materials |
| `SC6xxx` | shaders |
| `SC7xxx` | levels |
| `SC8xxx` | scripts |
| `SC9xxx` | the pack itself |

## Exit codes

| Code | Means |
|---|---|
| 0 | success |
| 1 | the cook failed |
| 2 | bad command line |
| 3 | a file could not be read or written |
