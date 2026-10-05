---
title: Sound entities
description: The entity class that plays a sound from a place in the level.
---

A sound is a point in the level that plays a sound file. Wires start and stop it, and it fires when the sound ends or reaches a marker. See [Entities and wiring](/concepts/entities-and-wiring/) for how wires work.

In the settings table, the first column is the name a level file uses and the second is the label in the Properties panel.

## point_sound

Plays a sound from where it stands: a hum, an alarm, a spoken line.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `sound` | Sound | empty | The sound file to play, as a path inside the project's content, such as `Sounds/door_open.wav`. |
| `volume` | Volume | 1 | How loud the sound is. 1 is the file as it is, 0 is silent. |
| `pitch` | Pitch | 1 | How fast the sound plays, from 0.1 to 10. 2 is twice as fast and an octave higher. |
| `mindistance` | Minimum distance | 2 | The sound is at full volume inside this distance. |
| `maxdistance` | Maximum distance | 30 | The sound is silent beyond this distance. |
| `looped` | Looped | 0 | Set to 1 and the sound repeats until something stops it. |
| `startplaying` | Start playing | 0 | Set to 1 and the sound plays when the level starts. |

Both distances are in units, and a unit is a metre. Between the two the volume falls off with distance and reaches nothing at the far one.

A volume below 0, or a pitch outside 0.1 to 10, is not used, and the default takes its place.

| Input | Does |
|---|---|
| `Play` | Plays the sound from its start. On a sound that is already playing it starts over. |
| `Stop` | Stops the sound. It fires nothing. |
| `SetVolume` | Sets the volume to the parameter, a number from 0 up. It holds for every later `Play` too. Anything else is ignored. |
| `SetPitch` | Sets the pitch to the parameter, a number from 0.1 to 10. What has played so far stays played, and the rest plays at the new speed. Anything else is ignored. |

| Output | Fires when |
|---|---|
| `OnEnded` | A sound that is not looped reaches its end. `Stop` does not fire it. |
| `OnMarker` | The sound reaches a marker. It carries the marker's name. |

The sound is its own activator for both outputs.

A sound sits on its node and goes where the node goes. Placed under a door in the scene tree, it moves with the door.

## Looped

A looped sound never ends by itself, so it never fires `OnEnded`.

A sound file can carry a loop region: the loop points an audio editor saves into a `.wav`. With one, a looped sound plays from the start of the file up to the region's end, then repeats the region. What comes after the region is never reached. Without one, the whole sound repeats.

A sound that is not looped plays once from start to end and ignores the loop region.

## Markers

A marker is a named moment in a sound file. [Cook and run a project](/guides/cook-and-run-a-project/) says how to put them there.

When the sound reaches a marker, `OnMarker` fires with the marker's name as its parameter. The name takes the place of whatever parameter the wire has. To tell markers apart, wire `OnMarker` to the `InValue` of a [`logic_case`](/reference/logic-entities/#logic_case) and give each case one marker's name. That is how a spoken line opens a door on one word and closes it on another.

Markers that are reached on the same tick fire in the order the file lists them. A marker at the very end of a sound fires just before `OnEnded`.

A looped sound fires its markers again on every pass. A marker before the loop region fires once, and one after it never fires. A marker at the very end of the region fires as each pass ends.

`Play` on a playing sound starts the markers over as well.

## Timing

The end of a sound and its markers are counted in ticks, from the length of the file. There are 60 ticks a second. A sound one second long fires `OnEnded` on the 60th tick after it starts, and on the 30th at pitch 2. A marker fires on the first tick that has played as far as the marker.

The count comes from the level's ticks, not from a clock or a sound device. A level that runs with no sound device, such as one on a server, fires on the same ticks.

Both outputs fire at the end of a tick, so what they are wired to gets its input on the next one.

## A sound that cannot play

If `sound` is empty, or the file cannot be loaded, the log says why once when the level starts and names the entity. `Play` then does nothing, and neither output fires.
