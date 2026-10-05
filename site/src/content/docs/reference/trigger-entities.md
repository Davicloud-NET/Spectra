---
title: Trigger entities
description: The entity classes that fire when the player walks into a volume.
---

A trigger is a volume: a part, or a group of parts, that is not drawn and not solid. It fires when the player walks into it. See [Entities and wiring](/concepts/entities-and-wiring/) for how an entity gets a shape.

In each settings table, the first column is the name a level file uses and the second is the label in the Properties panel.

## What they share

Only the player sets a trigger off. A touch starts when the player's body reaches the volume, and the volume is tested as its real shape, rotated or not.

A trigger senses with parts that have Touch events on. A block never senses.

Every trigger takes the same three inputs:

| Input | Does |
|---|---|
| `Enable` | Switches the trigger on. If the player is already inside, a touch starts on the next tick. |
| `Disable` | Switches the trigger off. If the player is inside, the touch ends there. |
| `Toggle` | Switches it the other way. |

The player is not an entity, so a trigger is its own activator. `!activator` on one of its wires names the trigger.

A volume has Seen by queries off, so it does not get in the way of the use key. The player can press a button that stands inside one.

## trigger_once

Fires the first time the player walks in, then switches itself off.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `startdisabled` | Start disabled | 0 | Set to 1 and the trigger senses nothing until it gets `Enable`. |

| Output | Fires when |
|---|---|
| `OnStartTouch` | The player comes inside. |
| `OnTrigger` | Together with `OnStartTouch`, straight after it. |

Switching itself off is the same as getting `Disable`. The trigger stays in the level, and `Enable` arms it for one more touch.

## trigger_multiple

Fires when the player walks in, keeps firing while the player stays, and fires when the player leaves.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `startdisabled` | Start disabled | 0 | Set to 1 and the trigger senses nothing until it gets `Enable`. |
| `wait` | Refire time | 1 | Seconds between `OnTrigger` fires while the player stays inside. 0 fires on the way in only. |

| Output | Fires when |
|---|---|
| `OnStartTouch` | The player comes inside. |
| `OnTrigger` | The player comes inside, and then every `wait` seconds until the player leaves. |
| `OnEndTouch` | The player leaves, or the trigger is disabled with the player inside. |

## trigger_teleport

Moves the player to another entity when the player walks in.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `target` | Destination | empty | The name of the entity the player is moved to, usually an `info_teleport_destination`. |
| `startdisabled` | Start disabled | 0 | Set to 1 and the trigger senses nothing until it gets `Enable`. |

| Output | Fires when |
|---|---|
| `OnStartTouch` | The player comes inside. By the time a wire delivers it, the player has moved. |
| `OnEndTouch` | The player is clear of the volume, which after a teleport is the next tick. Also when the trigger is disabled with the player inside. |

The player arrives standing still, with their feet at the destination and facing along its local +Z. A destination that points straight up or down leaves the facing as it was.

The destination is looked up at every touch. If several entities share the name, the first one in the scene tree is used. If none has it, nobody moves and the log warns once.

## info_teleport_destination

A point for a `trigger_teleport` to send the player to. It has no settings, takes no inputs and fires no outputs.

A teleport finds its destination by name, so any entity can stand in for one.
