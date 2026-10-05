---
title: Entities and wiring
description: Logic without code, by wiring one entity's output to another's input.
---

An entity is a node that does something while the game runs: a door, a trigger, a timer, a counter. You give it settings, then wire it to other entities.

## Outputs and inputs

An entity fires outputs when something happens to it. A timer fires `OnTimer` each time its interval runs out.

An entity also takes inputs, which tell it to do something. A counter takes `Add`.

A wire connects the two: when this output fires, send that input to that entity.

## An example

A timer ticks four times a second. Each tick goes through a relay and adds 2 to a counter. When the counter reaches 6, it switches the relay off, so the counting stops.

| Entity | Class | Wire |
|---|---|---|
| Ticker | `logic_timer` | `OnTimer` sends `Trigger` to Gate |
| Gate | `logic_relay` | `OnTrigger` sends `Add` with `2` to Tally |
| Tally | `math_counter` | `OnHitMax` sends `Disable` to Gate |

## What a wire holds

| Field | Meaning |
|---|---|
| Output | Which of this entity's outputs starts it. |
| Target | The name of the entity to send to. |
| Input | The input to send. |
| Parameter | A value to send along, if the input takes one. |
| Delay | Seconds to wait before sending. |
| Times | How many times the wire may fire. Forever by default. |

## Targets

The target is a node's name. If several nodes share the name, all of them get the input.

A name ending in `*` matches every name that starts that way: `door*` reaches `door1` and `door_left`.

Three special targets are worked out while the game runs:

| Target | Means |
|---|---|
| `!self` | The entity whose output is firing. |
| `!activator` | Whoever started the chain. |
| `!caller` | The entity that sent the input being handled. |

The activator travels along a chain. An entity that fires by itself, like a timer that ticks or a door that arrives, is its own activator. An entity that fires because an input reached it passes on the activator that came with the input.

The player is not an entity. So a trigger the player walks into is its own activator, and so is a button the player presses.

## Entities with a shape

A timer has no shape. A door does: it is a part that carries a `func_door` entity, and the entity slides the part. One node holds both the brush and the entity.

To turn geometry into an entity, select a block, a part or a group of them and press Make entity on the Build tab. Any block in the selection becomes a part, because only parts can move while a level plays. Remove entity takes the entity off again and leaves the parts as parts.

An entity owns the brush on its own node and the brushes below it in the scene tree, down to the next node that is an entity itself. A door made of three parts is a group with the entity on the group.

A trigger is a volume: a part that is not drawn and not solid, and that fires when the player walks into it. The editor shows it as a yellow outline. In the Properties panel, under Behavior, a volume has Collides, Seen by queries and Drawn off, and Touch events on.

An entity with no shape shows in the viewport as a green diamond.

## Playing does not change the level

Entities only run in play mode. They work on a copy of your settings and wires, so a counter that counted to 6 while you played is back at its start value when you stop. A door that slid open is back where you built it.

A wire with a limited number of fires is the same: the count runs down while you play and is whole again afterwards.

## Watching the wiring

The console can print every output that fires and every input that arrives while you play. Type `ent_watch on` in the editor's Console panel, then press Play. A wire aimed at a name nothing has prints a `miss` line. The <kbd>&#96;</kbd> key opens the panel while the level plays.

The console also lists a level's entities, shows one entity's wires and state, and sends an input by hand. See [Console](/reference/console/).

## Classes the engine doesn't know

If a level uses a class this build doesn't have, the entity is kept exactly as written, settings and wires included. It does nothing when you play, and the editor marks it.

The classes that exist today are listed in [Logic entities](/reference/logic-entities/), [Mover entities](/reference/mover-entities/), [Trigger entities](/reference/trigger-entities/), [Sound entities](/reference/sound-entities/) and [Player entities](/reference/player-entities/).
