---
title: Entities and wiring
description: Logic without code, by wiring one entity's output to another's input.
---

An entity is a node that does something while the game runs: a timer, a counter, a relay. You give it settings, then wire it to other entities.

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

## Playing does not change the level

Entities only run in play mode. They work on a copy of your settings and wires, so a counter that counted to 6 while you played is back at its start value when you stop.

A wire with a limited number of fires is the same: the count runs down while you play and is whole again afterwards.

## Classes the engine doesn't know

If a level uses a class this build doesn't have, the entity is kept exactly as written, settings and wires included. It does nothing when you play, and the editor marks it.

The classes that exist today are listed in [Logic entities](/reference/logic-entities/).
