---
title: Mover entities
description: Doors, buttons and pistons, the entity classes that slide a brush.
---

These classes slide the brush they are on. Each one is a part, or a group of parts, that carries the entity. See [Entities and wiring](/concepts/entities-and-wiring/) for how an entity gets a shape.

In each settings table, the first column is the name a level file uses and the second is the label in the Properties panel.

## What they share

Where you place the brush in the editor is one end of its trip: a door is built closed, a button is built out, a linear mover is built at position 0.

`movedir` is in the brush's own axes, so a rotated door slides along its rotated axis. Its length does not matter, only its direction. A direction of `0 0 0`, or one that cannot be read, is not used, and the default takes its place.

A trip takes its distance divided by its speed, rounded up to a whole tick. There are 60 ticks a second. The trip back takes as long as the trip out, and the brush ends on the spot you built it at.

Only parts move. If a block is still inside the entity, the brush stays where it is and the log names it. The editor's Problems panel lists these.

## func_door

A brush that slides open and closes again: a door, a gate, a hatch.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `movedir` | Move direction | `0 1 0` | The way the door slides to open. |
| `distance` | Distance | 0 | How far it slides. 0 uses the door's own size along the direction, less the lip. |
| `lip` | Lip | 0.05 | How much of the door is left showing when the distance comes from its size. |
| `speed` | Speed | 2 | Units a second. Anything below 0.01 counts as 0.01. |
| `wait` | Wait | 4 | Seconds the door stays open before it closes by itself. -1 keeps it open. |
| `startopen` | Start open | 0 | Set to 1 and the door is open when the level starts. It stays open until something closes it. |

A door with nothing set slides up by its own height, less 0.05.

| Input | Does |
|---|---|
| `Open` | Opens the door. A door that is closing turns round where it is. On a door that is open or opening it does nothing, and it does not restart the wait. |
| `Close` | Closes the door and cuts its wait short. A door that is still opening turns round where it is. On a door that is closed or closing it does nothing. |
| `Toggle` | Closes a door that is open or opening, and opens any other. |

| Output | Fires when |
|---|---|
| `OnOpen` | The door starts to open. |
| `OnClose` | The door starts to close, by an input or after its wait. |
| `OnFullyOpen` | The door arrives open. |
| `OnFullyClosed` | The door arrives closed. |

A door that starts open fires nothing when the level starts.

## func_movelinear

A brush that slides to wherever it is sent and rests there: a piston, a sliding wall, a gate that stops half way.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `movedir` | Move direction | `0 1 0` | The way the brush slides towards position 1. |
| `distance` | Distance | 4 | How far position 1 is from position 0. |
| `speed` | Speed | 2 | Units a second. Anything below 0.01 counts as 0.01. |
| `startposition` | Start position | 0 | Where along the way the brush is when the level starts, from 0 to 1. |

| Input | Does |
|---|---|
| `Open` | Heads for position 1 from wherever the brush is. |
| `Close` | Heads for position 0 from wherever the brush is. |
| `SetPosition` | Heads for the parameter, a number from 0 to 1. A number outside that range goes to the nearer end. Anything that is not a number is ignored. |

| Output | Fires when |
|---|---|
| `OnFullyOpen` | The brush arrives at position 1. |
| `OnFullyClosed` | The brush arrives at position 0. |

Stopping anywhere between the two ends fires nothing. Neither does a start position.

## func_button

A brush that goes in when it is pressed and comes back out by itself: a wall button, a floor plate, a switch.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `movedir` | Move direction | `0 0 -1` | The way the button goes in. |
| `distance` | Distance | 0 | How far it goes in. 0 uses the button's own size along the direction, less the lip. |
| `lip` | Lip | 0.02 | How much of the button is left standing out when the distance comes from its size. |
| `speed` | Speed | 1 | Units a second. Anything below 0.01 counts as 0.01. |
| `wait` | Wait | 1 | Seconds the button stays in before it comes back out. -1 keeps it in. |

| Input | Does |
|---|---|
| `Use` | Presses the button. This is what the player's use key sends. |
| `Press` | Presses the button. The same thing, for a wire. |

| Output | Fires when |
|---|---|
| `OnPressed` | A press is taken and the button starts to go in. |
| `OnIn` | The button arrives all the way in. |
| `OnOut` | The button arrives back out. |

The button counts as pressed from the press until it is back out and at rest. A press in that time does nothing: it does not turn the button round and it does not restart the wait.

The player presses a button by looking at it from within 2 units and pressing <kbd>E</kbd>.
