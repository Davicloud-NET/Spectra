---
title: Captions and subtitles
description: Put what a sound says into words, in more than one language.
---

A caption says in words what a sound is: a line someone speaks, a door that opens. Captions are for players who cannot hear the game, or who play with the sound off.

:::note
Nothing draws captions on screen yet. Each caption is written to the log when it appears, and the engine keeps the list of captions that show now for whatever will draw them. See [Where captions go](#where-captions-go).
:::

## Two kinds

```
Assets/
  Captions/
    en.txt                      sound captions, one file a language
  Sounds/
    door_open.wav
    vo/
      guard_hey.wav
      guard_hey.en.vtt          this voice file's subtitles
```

A sound with a line in the caption file has a sound caption: a few words for what is heard, such as "Door opens".

A sound with a subtitle file beside it is speech. Its subtitles are the words that are said, each line at its own time, with the speaker's name.

Which kind a sound is comes from where its text lives. Nothing inside the text says so.

## Sound captions

A language's sound captions are one text file, `Captions/<language>.txt`. Each line is the sound's path, an equals sign and the words:

```
// Captions/en.txt
Sounds/door_open.wav = Door opens
Sounds/lift_hum.wav = Lift hums
Sounds/radio.wav = Radio crackles \n A voice fades in
```

- The file is UTF-8.
- The path is the sound's path from the `Assets` folder, the one a [sound entity](/reference/sound-entities/) is set to.
- The words are everything after the first equals sign.
- `\n` is a line break.
- A line that starts with `//` is a comment. Blank lines are fine.
- If a sound is named twice, the later line is used.

A line that cannot be read is skipped with a warning, and the rest of the file still works.

## Voice subtitles

Subtitles are a WebVTT file beside the voice file. It has the voice file's name, then the language, then `.vtt`: `guard_hey.wav` has `guard_hey.en.vtt`.

```
WEBVTT

00:00.000 --> 00:01.400
<v Guard>Hey! You there!

00:01.900 --> 00:03.600
<v Guard>Stop right where you are.
```

WebVTT is the web's subtitle format. Subtitle editors and speech-to-text tools can write it.

The engine reads a small part of it:

| Part | How it is written |
|---|---|
| The header | `WEBVTT` on the first line. |
| A cue | One line of times, then one or more lines of words. Cues are apart by a blank line. |
| Times | Minutes, seconds and milliseconds, `00:01.400`. Hours in front are optional, `00:00:01.400`. The arrow has a space on each side. |
| A cue's name | Optional, on a line of its own above the times. It is not shown. |
| The speaker | `<v Name>` at the start of the words. The tag is taken out and the name is kept. `</v>` at the end is optional. |
| A note | A block that starts with `NOTE`. It is skipped. |
| Special letters | `&amp;` for `&`, `&lt;` for `<`, `&gt;` for `>` and `&nbsp;` for a space that does not break. |

Line breaks in a cue's words are kept.

Anything else WebVTT allows is passed over, and the words are still shown: settings after the times, `STYLE` and `REGION` blocks, and tags such as `<i>`, which are taken out. The cook warns about each, so you know the engine did not use it.

A `<` that opens no tag is shown as it is, and the cook warns about it: a `<` with no `>` after it on the line, or one that no name follows. Write `&lt;` for the sign. A `<v Guard` with its `>` left out shows as written and names no speaker.

A file is refused, with the file and the line, when its cues cannot be told apart or timed:

- it does not start with `WEBVTT`
- a block has no line of times
- the arrow has no space on each side
- a time cannot be read
- a cue does not end after it starts

A refused file shows no subtitles, and the cook fails on it. The log warns about it too, with the line, the first time its sound is heard in each run of a level.

If a sound has both a subtitle file and a line in the caption file, the subtitle file is used.

## Which captions show

One setting says which captions show:

| Setting | Shows |
|---|---|
| `off` | Nothing. |
| `voice` | Speech only. This is what the engine starts with. |
| `all` | Speech and sound captions. |

Set it from the [console](/reference/console/#captions):

```
captions all
```

## When a caption shows

These rules are the engine's, so every game's captions follow them however they are drawn.

- A caption shows only while its sound can be heard where the listener stands. How far a sound carries is its own [maximum distance](/reference/sound-entities/), not a fixed radius. A sound that is silent because it is far away, turned down or over has no caption.
- Heard means loud enough to notice: a caption comes up once the sound arrives at half a percent of its full volume or more, and is let go when it falls under a quarter of a percent. A sound behind a thick wall still has its caption. The gap keeps a sound at the edge of hearing from showing its caption over and over. The engine goes by the volume it plays the file at, not by how loud the recording is.
- A voice line shows when the sound reaches the line's start time, never before. It goes at its end time.
- A caption stays long enough to be read: at least a second, and longer for more words. The count is half a second and then fifteen letters a second. A line that is over sooner stays until it has been read.
- A sound that plays again while its caption still shows keeps that one caption up for longer. It does not add a second one, so a run of footsteps is one caption.
- A sound that plays once keeps its sound caption for as long as it plays. A looped sound shows its caption for its reading time each time it comes into hearing, not for as long as it plays.
- Captions are listed in the order they started.

A line's time is counted in the level's ticks, the way a sound's [markers](/reference/sound-entities/#timing) are. A frame that hangs holds the ticks back while the sound plays on. For a sound of up to ten seconds that plays once, the engine moves the lines on by what was heard meanwhile. A longer or looped sound is played as a stream, and after such a frame its lines can show a little later than they are heard.

## Languages

A language is a short lowercase tag: `en`, `de`, `pt-br`.

The project names its own language in the [project file](/concepts/projects-and-levels/#language). With none named it is `en`.

Captions are looked up in the project's language until you set another:

```
caption_language de
```

A sound with no caption in the language that is set gets its caption in the project's language. If that has none either, the sound has no caption. Sound captions and subtitles fall back one sound at a time, so a half-finished translation shows what it has.

### Add a translation

1. Copy `Captions/en.txt` to `Captions/de.txt` and translate the words. Leave the paths as they are.
2. For each voice file, copy its `.en.vtt` to `.de.vtt` beside it and translate the words. Change the times if the line is spoken differently.
3. Cook the project. The cook says how many sounds have a caption in the project's language and none in the new one, and names them. It says the same for subtitle files, and names the files that are not there.

The cook sets caption files against caption files and subtitle files against subtitle files. A voice with subtitles in the project's language needs a subtitle file in the new one. A line in the new caption file does not count for it: that would be a sound caption, and `captions voice` does not show those.

## Find what is missing

[`captions_missing`](/reference/console/#captions_missing) lists the sounds the level's entities are set to play that have no caption in the language that is set:

```
> caption_language de
> captions_missing
captions_missing: no caption in de for 2 of 4 sounds the level plays.
Sounds/alarm.wav  no caption
Sounds/door_open.wav  none in de, shows the one in en
```

The cook checks the files too. These are its warnings, and `--strict` makes them errors:

| Code | Means |
|---|---|
| `SC4101` | A line of a caption file is not a path, an equals sign and words. It is skipped. |
| `SC4102` | A caption file names one sound twice. The later line is used. |
| `SC4103` | A caption line or a subtitle file is for a sound that is not in the project. |
| `SC4104` | A sound has a caption line and a subtitle file in the same language. The subtitle file is used. |
| `SC4105` | A text file in `Captions` is not named after a language, so it is never read. |
| `SC4106` | Sounds have a caption in the project's language and none in this file's. It names them. |
| `SC4109` | A subtitle file uses a part of WebVTT the engine does not read. The words still show. |
| `SC4110` | A subtitle line starts after its sound has ended, so it never shows. |
| `SC4111` | A subtitle file has no language in its name, so it is never read. |
| `SC4112` | A subtitle file has no line with words in it. |
| `SC4113` | A subtitle line runs more than a second past the end of its sound. It still shows. Check that the file is for this recording. |
| `SC4114` | Sounds have subtitles in the project's language and none in another language. It names the files that are not there. |

A subtitle line may run up to a second past the end of its sound with no warning. Subtitle tools pad a last line so it can be read, and the engine keeps a line up for its reading time anyway.

`SC4108` is an error: a subtitle file the engine refuses. `SC4107` is a note that says how many sounds a caption file covers.

Caption and subtitle files go into the pack as the text you wrote.

## Where captions go

### The log

Each caption is written to the log once, when it appears. A project that runs from its cooked pack prints the log in its terminal. The editor writes each caption into its Output panel while the level plays.

```
Caption: Guard: Hey! You there!
Caption: [Door opens]
```

A voice line is the speaker and the words. A sound caption is in square brackets.

### The feed

The engine keeps the captions that show right now in a list, and that list is all a caption view needs. The engine's own view will read it when the engine can draw one. A game can restyle that view, or draw captions itself from the same list.

In code the list is `Engine.Captions`, a `CaptionFeed`. A program that hosts the engine on another thread, as the editor does, reads the same list from `FrameSnapshot.Captions`. A game that draws captions itself sets `Engine.LogCaptions` to false.

Each caption in the list holds:

| Field | Holds |
|---|---|
| `Id` | A number that stays the same for as long as the caption shows, so a view can fade it in and out. A caption that shows again later has a new one. |
| `Kind` | `Voice` or `Sound`. |
| `Text` | The words, with the writer's line breaks. |
| `Speaker` | The speaker's name, or nothing. |
| `Position` | Where the sound is in the level, or nothing for a sound with no place, such as a stereo file. |
| `Audibility` | How well the sound is heard, from 0 to 1. |
| `StartedAt` | When the caption started to show, in seconds on the feed's clock. |
| `EarliestEnd` | The earliest it may go, on the same clock. |

Nothing about looks is in the list: no colours, no sizes, no place on the screen. Those belong to the view.
