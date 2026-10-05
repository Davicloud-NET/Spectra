---
title: Sound entities
description: The entity class that plays a sound from a place in the level.
---

A sound is a point in the level that plays a sound file. Wires start and stop it, and it fires when the sound ends or reaches a marker. See [Entities and wiring](/concepts/entities-and-wiring/) for how wires work.

:::note
A sound is heard in the editor when you press Play, and in a game that runs from its cooked pack. The engine plays cooked sounds only, so the editor cooks a sound file the first time a level uses it. See [Cook and run a project](/guides/cook-and-run-a-project/).
:::

In the settings table, the first column is the name a level file uses and the second is the label in the Properties panel.

## point_sound

Plays a sound from where it stands: a hum, an alarm, a spoken line.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `sound` | Sound | empty | The sound file to play, as a path inside the project's content, such as `Sounds/door_open.wav`. |
| `volume` | Volume | 1 | How loud the sound is. 1 is the file as it is, 0 is silent. Above 1 it is no louder up close, and it stays at full volume further out. |
| `pitch` | Pitch | 1 | How fast the sound plays, from 0.1 to 10. 2 is twice as fast and an octave higher. |
| `mindistance` | Minimum distance | 2 | The sound is at full volume inside this distance. |
| `maxdistance` | Maximum distance | 30 | The sound is silent beyond this distance. |
| `looped` | Looped | 0 | Set to 1 and the sound repeats until something stops it. |
| `startplaying` | Start playing | 0 | Set to 1 and the sound plays when the level starts. |
| `placed` | Placed | 1 | The sound is heard from where it stands. Set to 0 and it plays in both ears. |
| `fades` | Fades with distance | 1 | The sound gets quieter with distance. Set to 0 and it is as loud everywhere as it is up close. |
| `walls` | Muffled by walls | 1 | Walls between the sound and the listener make it quieter and duller. Set to 0 and it passes through them. |
| `doppler` | Doppler | 1 | The pitch rises while the sound and the listener close in and falls while they part. Set to 0 and it keeps its own pitch. |

Both distances are in units, and a unit is a metre. The volume falls off between the two and reaches nothing at the far one.

Select a sound in the editor and the viewport draws both distances round it.

A volume below 0, or a pitch outside 0.1 to 10, is not used, and the default takes its place.

| Input | Does |
|---|---|
| `Play` | Plays the sound from its start. On a sound that is already playing it starts over. |
| `Stop` | Stops the sound. It fires nothing. |
| `SetVolume` | Sets the volume to the parameter, a number from 0 up. It holds for every later `Play` too. Anything else is ignored. |
| `SetPitch` | Sets the pitch to the parameter, a number from 0.1 to 10. What has played so far stays played, and the rest plays at the new speed. It holds for every later `Play` too. Anything else is ignored. |

| Output | Fires when |
|---|---|
| `OnEnded` | A sound that is not looped reaches its end. `Stop` does not fire it. |
| `OnMarker` | The sound reaches a marker. It carries the marker's name. |

The sound is its own activator for both outputs.

A sound sits on its node and goes where the node goes. Placed under a door in the scene tree, it moves with the door.

## What is heard

The audio device plays up to 32 sounds at once. When a level plays more, the loudest at the listener are heard. The rest keep counting, so a looped sound is in step when the listener walks up to it. [`sound_stats`](/reference/console/#sound_stats) prints how many are heard.

A mono file is heard from where the sound stands. A stereo file is not placed. It plays in both ears, and only its volume follows the distance.

A sound can have a caption that says in words what is heard, for players who cannot hear it. See [Captions and subtitles](/guides/captions/).

## What is simulated

Four things are worked out for a sound on its way to the listener. Each has a setting of its own, and all four start at 1.

| Setting | At 1 | At 0 |
|---|---|---|
| `placed` | The sound is heard from where it stands. | It plays in both ears, the same from every side. |
| `fades` | It gets quieter with distance, between its two distances. | It is as loud everywhere as it is up close. |
| `walls` | Walls between the sound and the listener make it quieter and duller. | It passes through walls as if they were not there. |
| `doppler` | Its pitch rises while it and the listener close in, and falls while they part. | It keeps its own pitch. |

Walls do not muffle a sound yet. Until they do, `walls` changes nothing.

The four do not depend on each other. Some combinations a level needs:

- An alarm heard through a whole building: `fades` at 0. It is as loud in every room, and it still comes from the alarm's side.
- Rain, room tone or music that belongs to a place: `placed` at 0. It fills both ears, and it fades as the player walks away.
- A narrator, or a voice in the player's head: `placed` and `fades` at 0. It sounds the same wherever the player stands.
- A radio in a passing car whose music should not bend: `doppler` at 0.

A stereo file is never placed, whatever `placed` says. A sound that is not placed has no Doppler either: it has no way to the listener that could get longer or shorter.

Doppler is the change of pitch a moving sound has: a siren is higher while it comes at you and lower once it has passed. The engine works it out from how fast the way from the sound to the listener gets longer or shorter, so it makes no difference which of the two moves. Sound travels 343 units a second. Closing in at 20 units a second raises the pitch by about a semitone, and at walking speed the change is hard to hear. The pitch never goes more than an octave up or down.

Doppler changes what is heard and nothing else. The end of a sound and its markers are counted from the sound's own pitch, as [Timing](#timing) says, so `OnEnded` and `OnMarker` fire on the same ticks with Doppler at 1 or at 0. A sound that plays once while the listener closes in fast is over in the ears a little before `OnEnded` fires. One heard while they part fast is still playing then, and is cut off a quarter of a second later.

A caption of a sound that is not placed has no place either, like the caption of a stereo file.

The console switches each of the four off for every sound at once, to hear what it does. See [`sound_simulate`](/reference/console/#sound_simulate) and [`sound_doppler`](/reference/console/#sound_doppler).

## Looped

A looped sound never ends by itself, so it never fires `OnEnded`.

A sound file can carry a loop region: the loop points an audio editor saves into a `.wav`. With one, a looped sound plays from the start of the file up to the region's end, then repeats the region. What comes after the region is never reached. Without one, the whole sound repeats.

A sound that is not looped plays once from start to end and ignores the loop region.

## Markers

A marker is a named moment in a sound file. [Cook and run a project](/guides/cook-and-run-a-project/) says how to put them there.

When the sound reaches a marker, `OnMarker` fires with the marker's name as its parameter. The name takes the place of whatever parameter the wire has. To tell markers apart, wire `OnMarker` to the `InValue` of a [`logic_case`](/reference/logic-entities/#logic_case) and give each case one marker's name. That is how a spoken line opens a door on one word and closes it on another.

Markers that are reached on the same tick fire in the order the file lists them. A marker at the very end of a sound fires just before `OnEnded`.

A looped sound fires its markers again on every pass. A marker before the loop region fires once, and one after it never fires. A marker at the very end of the region fires as each pass ends.

A loop shorter than a tick is the exception. It turns round more than once in a tick, and a marker in it then fires at most twice a tick, not once for every pass.

`Play` on a playing sound starts the markers over as well.

## Timing

The end of a sound and its markers are counted in ticks, from the length of the file. There are 60 ticks a second. A sound one second long fires `OnEnded` on the 60th tick after it starts, and on the 30th at pitch 2. A marker fires on the first tick that has played as far as the marker.

The count comes from the level's ticks, not from a clock or a sound device. A level that runs with no sound device, such as one on a server, fires on the same ticks.

Both outputs fire at the end of a tick, so what they are wired to gets its input on the next one.

An input that arrives on the tick a sound would end is handled before the end. `Stop` on that tick means `OnEnded` does not fire. `Play` on that tick starts the sound over, and the end it was about to reach does not fire either. Markers the sound had not reached by then are skipped.

## A sound that cannot play

If `sound` is empty, or the file cannot be loaded, the log says why once when the level starts and names the entity. `Play` then does nothing, and neither output fires. A `.wav` that has not been cooked is such a file.
