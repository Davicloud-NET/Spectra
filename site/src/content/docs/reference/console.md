---
title: Console
description: The engine's command line, where to type and what each command prints.
---

The console is the engine's command line. Most of its commands today are about entities and their wiring: they list what a level holds, show one entity in full, send an input by hand, and print what fires while the level plays.

## Where to type

### The editor

Open the Console panel with View, Panels, Console, or press <kbd>&#96;</kbd>. Type a line and press <kbd>Enter</kbd>. <kbd>Up</kbd> and <kbd>Down</kbd> bring back earlier lines.

The panel also runs the editor's own words, such as `undo` and `snap off`. A line that starts with one of those is the editor's. Any other line goes to the engine as you typed it. `help` prints both lists, the editor's first. The editor's words go one to a line: the syntax below is the engine's.

Replies show in the Console and Output panels, which share one list of 500 lines. Everything the engine's console prints also goes to the log file in the `logs` folder, marked `[console]`.

While the level plays, the game holds the mouse. Press <kbd>&#96;</kbd> to take it back: the Console panel opens with the caret in it, and the level keeps running while you type. Click in the view to walk again.

On a German keyboard that key is <kbd>ö</kbd>.

### The demo

```bash
dotnet run --project SpectraEngine.Executable -- d3d11 --play --command="ent_watch on" --command="wait; ent_list"
```

Each `--command` is one line. The lines run in the order given, once the level has loaded and before `--play` starts it. A line that needs the running level starts with `wait`.

Replies go to the log, marked `[console]`. An error reads `[console] error:` and a warning `[console] warning:`.

Add `--console` and the demo also reads lines from the terminal it was started in, for as long as it runs. That is how you send an input to a level while you watch it in the demo's window.

## Syntax

- `;` separates commands on one line.
- `//` starts a comment that runs to the end of the line.
- Double quotes make one argument out of several words: `ent_fire "Main Door" Open`. Inside quotes, `;` and `//` are plain text, `\"` is a quote and `\\` is a backslash.
- `""` is an empty argument.
- A quote that is never closed runs to the end of the line, with a warning.
- Command names can be typed in any case. Entity names, class names and input names cannot.
- Numbers use a dot for the decimal point, on every machine: `0.5`.
- A line that starts with `>` is kept for scripts and is refused today.

A command that fails prints an error, and the rest of the line still runs.

## Commands

| Command | Does |
|---|---|
| `help [command]` | Lists the commands, or prints one command's usage. |
| `echo <text>` | Prints its arguments. |
| `wait [frames]` | Runs everything after it one frame later, or as many frames later as you give. |
| `ent_fire <target> <input> [parameter] [delay]` | Sends an input to every entity the target names. |
| `ent_list [pattern]` | Lists entities by name and class. |
| `ent_show <pattern>` | Prints an entity's settings, state, wires and inputs. |
| `ent_watch [on \| off \| <pattern> ...]` | Prints what entities fire and receive as it happens. |
| `sound_stats` | Prints how many sounds the level is playing and how many of them are heard. |
| `sound_simulate [placed \| fades \| walls \| doppler \| all] [on \| off]` | Switches a part of the sound simulation on or off for every sound. |
| `sound_doppler [strength]` | Sets how strong Doppler is, from 0 to 4. |
| `fake_device_loss` | Ends the next frame the way a lost graphics device does, to test what happens then. The demo ends with an error. The editor restarts its viewport and keeps the level. |
| `captions [off \| voice \| all]` | Sets which captions show: none, speech only, or speech and other sounds. |
| `caption_language [language]` | Sets the language captions are looked up in. |
| `captions_missing` | Lists the sounds the level's entities play that have no caption. |

Entities only run while the level plays, so the four `ent_` commands answer differently before and after you press Play:

| Command | While editing | While playing |
|---|---|---|
| `ent_fire` | An error. There is nothing to send to. | Queues the input and says how many entities it reaches. |
| `ent_list` | The entities placed in the level, marked `not running`. | The running entities, with the tick, the time and how many events are waiting. |
| `ent_show` | Settings and wires as you built them, and no state. | Settings, live state, and how many fires each wire has left. |
| `ent_watch` | Sets the watch. Nothing prints until the level runs. | Sets the watch. Lines print from the next event on. |

### ent_fire

```
> ent_fire score Add 5
ent_fire: Add("5") queued for 1 entity named score
ent 1 send console -> score.Add("5")
```

The first reply comes at once. The `send` line comes on the next tick, when the input arrives, whether the watch is on or not.

The delay is in seconds. To give a delay with no parameter, put `""` where the parameter goes:

```
> ent_fire door* Open "" 2
ent_fire: Open queued for 3 entities matching door*, in 2s
```

A target is a name, as on a wire. It cannot be a class, and it cannot be `!self`, `!activator` or `!caller`, which mean nothing on the console.

An input that none of the entities has is refused before anything is sent:

```
> ent_fire relay1 trigger
ent_fire: logic_relay has no input 'trigger'. Did you mean Trigger? Inputs: Trigger Enable Disable Toggle
```

When a class's inputs are not known, the input is sent anyway and a `deny` line says how it went.

### ent_list

```
> ent_list
ent_list: 3 placed, not running
relay1  logic_relay
score  math_counter
spawner  crate_spawner  (class not in this build)
```

Each row is a name, then a class. While the level plays the first line reads like this:

```
ent_list: 2 running, tick 2 (0.03 s), 1 event waiting
```

With a pattern it counts the matches: `ent_list: 1 of 4 running match math_counter`.

### ent_show

```
> ent_show score
score: math_counter, node 3f2a9c1e-5b7d-4e0a-9c41-0d2f6a8b1c33
score keyvalue startvalue = 0 (default)
score keyvalue min = 0 (default)
score keyvalue max = 3
score state value = 3
score state min = 0
score state max = 3
score state refused inputs = 0
score think: none
score output OutValue: not wired
score output OnHitMax -> door*.Open in=2s fires=2 of 3 left, 2 entities matching door*
score output OnHitMax -> dor1.Close fires=unlimited, nothing is named dor1
score output OnHitMin: not wired
score inputs: Add Subtract SetValue SetValueNoFire SetHitMax SetHitMin GetValue
```

Every row starts with the entity's name.

| Row | Holds |
|---|---|
| `keyvalue` | A setting. `(default)` means the level does not set it. |
| `state` | A value the running entity holds now. |
| `think` | When the entity next wakes by itself, such as a timer's next fire. |
| `output` | One wire: where it goes, its delay, the fires it has left, and how many entities its target reaches now. An output with no wire reads `not wired`. |
| `inputs` | The inputs the class takes. |

Before you press Play there are no `state` and `think` rows. One row reads `score not running: no live state` in their place, and a wire shows its fires as a plain count.

### ent_watch

```
> ent_watch
ent_watch: on, every entity. Nothing prints until the level runs.

> ent_watch door* relay1
ent_watch: on, names matching door* or relay1. Nothing prints until the level runs.

> ent_watch off
ent_watch: off
```

`ent_watch` alone means `on`. Each form sets the whole watch, and a new list of patterns replaces the old one. The watch stays on from one play to the next.

With patterns, a line prints when a pattern matches the sender or the receiver, by name or by class. It also prints when a pattern matches the target as the wire spells it, so a wire to a misspelled name shows under a pattern for that name.

### sound_stats

```
> sound_stats
sound_stats: 5 playing, 2 with a source, 1 without, 1 silent, 1 not loaded.
sound_stats: 2 sources, 0 starts refused.
```

| Count | Means |
|---|---|
| `playing` | Sounds the level is playing, heard or not. |
| `with a source` | Sounds that are heard. Each one holds a source of the audio device. |
| `without` | Sounds loud enough to hear that are not heard, because louder ones hold every source. |
| `silent` | Sounds with nothing to hear: too far away, over, or on a node that was deleted. |
| `not loaded` | Sounds whose file could not be loaded. The log names each file once. |
| `sources` | How many sounds the audio device can play at once. |
| `starts refused` | Sounds the device had no source for when it was asked to start them. |

Before you press Play it prints `sound_stats: the level is not running, so it plays nothing.` and the number of sources. With no audio device it says so first, as a warning.

### sound_simulate

```
> sound_simulate
sound_simulate: placed on, fades on, walls on, doppler on.

> sound_simulate fades off
sound_simulate: placed on, fades off, walls on, doppler on.

> sound_simulate all on
sound_simulate: placed on, fades on, walls on, doppler on.
```

Alone it prints the four parts. With a name and `on` or `off` it sets that part, and `all` sets the four together. [What is simulated](/reference/sound-entities/#what-is-simulated) says what each part does.

It is for listening: switch a part off, hear the level without it, and switch it on again. Every sound has the same four as settings of its own. A part is heard when it is on here and on the sound, so `off` here switches it off for every sound, and `on` leaves each sound to its own setting.

A wrong name or word is refused: `sound_simulate: 'echo' is not a part. Give placed, fades, walls, doppler or all, then on or off.`

The engine starts with all four on.

### sound_doppler

```
> sound_doppler
sound_doppler: 1. 1 is the real shift and 0 is none.

> sound_doppler 2
sound_doppler: 2. 1 is the real shift and 0 is none.
```

Alone it prints the strength. A number from 0 to 4 sets it. 1 is what a moving sound really does, 0 is no shift, and 2 doubles the shift: a sound that was a semitone high is two semitones high. Whatever the strength, the pitch never goes more than an octave up or down. Anything else is refused: `sound_doppler: 'loud' is not a number from 0 to 4.`

While Doppler is switched off, a second line says so: `sound_doppler: Doppler is switched off. 'sound_simulate doppler on' switches it on.`

The engine starts at 1.

### captions

```
> captions
captions: voice. Speech shows. Other sounds do not.

> captions all
captions: all. Speech and other sounds show.

> captions off
captions: off. No captions show.
```

Alone it prints the setting. The engine starts at `voice`. A kind that is switched off goes at once, and one that is switched on shows for the sounds that are heard already.

Nothing draws captions on screen yet. Each one is written to the log when it appears, as `Caption: Guard: Hey! You there!` for speech and `Caption: [Door opens]` for another sound. [Captions and subtitles](/guides/captions/) says where the words come from.

### caption_language

```
> caption_language
caption_language: en, the project's language.

> caption_language de
caption_language: de. A sound with no caption in de gets the one in en, the project's language.
```

A language is a short tag such as `en`, `de` or `pt-br`. Capitals are turned into small letters. Anything else is refused.

If the project has no caption file for the language, a second line says so as a warning: `caption_language: the project has no Captions/de.txt.` The language is set all the same, because a voice file can have subtitles in it.

Captions that show are looked up again in the new language on the next frame.

### captions_missing

```
> captions_missing
captions_missing: no caption in de for 2 of 4 sounds the level plays.
Sounds/alarm.wav  no caption
Sounds/door_open.wav  none in de, shows the one in en
```

It looks at every sound an entity in the level is set to play, in the language `caption_language` has set, and works before and after you press Play. A sound counts as having a caption when it has a line in the language's caption file or a subtitle file in the language.

When nothing is missing it prints `captions_missing: none. Every sound the level plays has a caption in en (4 sounds).` It lists up to 200 sounds.

While the level is not running it reads the caption files again each time, so you can fix a file and ask again.

## Watch lines

Every line is `ent`, the tick, a verb, and what happened. Ticks count from 0, where the level starts, at 60 a second.

| Verb | Means |
|---|---|
| `fire` | An output fired. `wires` is how many wires it sent along. `spent` counts wires that have used up their fires. |
| `send` | An input arrived at one entity. A name that reaches three entities prints three lines. |
| `wait` | A wire with a delay is waiting. `in` is the delay in seconds. Its `send` prints when the delay is over. |
| `miss` | An input came due and nothing had the target's name. |
| `deny` | An entity got an input its class does not take. It follows the `send`. |
| `drop` | The tick had more lines than the limit. The line says how many were left out. |

`miss`, `deny` and `drop` are warnings.

The sender is written `name.Output`. An input from `ent_fire` has the sender `console`. One the engine sent itself, such as the player's use key, has the sender `game`.

`by=` names the activator, and is left out when the activator is the sender. A parameter follows its input in brackets: `Add("5")`. A name is put in quotes when it is empty or holds a space, a dot or a quote.

This level has a `logic_auto` named `auto` that triggers `relay1`. The relay opens `door1` after two seconds, adds 5 to `counter`, and has a third wire to `dor1`, which nothing is called:

```
ent 0 fire auto.OnMapSpawn wires=1
ent 1 send auto.OnMapSpawn -> relay1.Trigger
ent 1 fire relay1.OnTrigger wires=3 by=auto
ent 1 wait relay1.OnTrigger -> door1.Open in=2s
ent 1 send relay1.OnTrigger -> counter.Add("5") by=auto
ent 1 fire counter.OutValue wires=0 by=auto
ent 1 miss relay1.OnTrigger -> dor1.Close  nothing is named dor1
```

Two seconds later, around tick 121, the waiting wire arrives:

```
ent 121 send relay1.OnTrigger -> door1.Open by=auto
```

## Names and patterns

Names are case sensitive: `Door1` is not `door1`. So are inputs: `trigger` is not `Trigger`.

A name ending in `*` matches every name that starts that way. `*` alone matches everything.

If several entities share a name, all of them match.

`ent_fire` matches names only, by the same rule as a wire. `ent_list`, `ent_show` and `ent_watch` also match a class, so `ent_list logic_*` lists every logic entity.

A name with a space needs quotes: `ent_show "Main Door"`.

## Limits

| What | Limit |
|---|---|
| Watch lines in one tick | 64, then one `drop` line |
| Rows from `ent_list` | 200, then a line that says how many more there are |
| Entities from `ent_show` | 5, then a line that says how many more match |
| Sounds from `captions_missing` | 200, then a line that says how many more there are |
| Commands run in one frame | 512. The rest run on the next frame. |
| Frames a `wait` can hold | 300 |
