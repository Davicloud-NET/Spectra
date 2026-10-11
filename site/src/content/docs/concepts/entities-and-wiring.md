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
| Delay | Seconds to wait before sending, counted in whole ticks. |
| Times | How many times the wire may fire. Forever by default. |

A level runs at 60 ticks a second, and every wait is a whole number of ticks. A delay that falls between two ticks waits for the later one: 0.5 is 30 ticks, and 0.04 is 3 ticks, which is 0.05 seconds. Any delay above zero is at least one tick. A timer's interval and a trigger's refire time are counted the same way.

Inputs that land on the same tick arrive in the order they were sent, which for one output's wires is the order they are listed in. A shorter delay does not go first: 0.02 and 0.03 are both 2 ticks. To make sure one input follows another, give it a delay at least a tick longer.

## What an input takes

Each input takes one kind of parameter, or none. A counter's `SetValue` takes a number. A branch's `SetValue` takes `0` or `1`. A door's `Open` takes nothing. A counter's `Add` takes a number or nothing, and adds 1 when it gets nothing.

Some outputs send a value of their own. A counter's `OutValue` sends its number, and a sound's `OnMarker` sends the marker's name. That value takes the place of the wire's parameter, so whatever the Parameter field of such a wire holds is not used.

The reference pages list both for every class, in the Takes and Sends columns.

The editor reads a wire's parameter against them. A line under the wire, in the Sends section of the Properties panel, says when the parameter will not do what it looks like it does:

| The line | Means |
|---|---|
| `SetValue takes a number, not 'five'` | The input cannot read what you typed. |
| `SetValue needs a number` | The input needs a parameter and the wire has none. |
| `Open takes no parameter` | The input ignores what you typed. |
| `OutValue sends its own value, so this parameter is not used` | The output's value arrives, not what you typed. |

The line is a warning and nothing more. The wire is kept as you typed it, and a level that already holds such a wire loads and saves as it was.

The first three lines are about the input, so the editor has to know which class answers the wire. It cannot tell when the target is a prefix, `!activator` or `!caller`, a name that several entities share, or an entity of a class this build does not know. It says nothing about the input then. The same goes for a level with more than 2000 entities, where the editor does not list them all and a second entity of that name may be among the rest.

The last line is about the output alone, so it shows whatever the target is.

When the level plays, an input gets its parameter as text and reads it itself. What it does with text it cannot read is listed with each class.

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

An entity with no shape shows in the viewport as its class's icon, which always faces you. It is the icon the class has in the scene tree, the Logic view and the Entity list, and it is drawn in the colour of the class's group, so a sound, a player start and a timer can be told apart at a glance. A class whose facing matters, such as a player start or a teleport destination, also gets an arrow that shows which way it points. A sound has a place and no direction, and a logic entity has neither, so they get no arrow. Far away, the icon becomes a small diamond in the same colour.

Icons that would cover each other, such as the three sounds of one lift, are drawn side by side in the order of the scene tree, and a light's icon joins the same row. An icon next to such a row moves aside for it. A small mark and thin lines show where they really are, and a click or a marquee takes an icon where it is drawn. The move handles of a selected one stay on its real place, in the middle of the row. Drag a handle to move the entity, or click an icon under the handles to select that one.

A class this build does not know, such as one from another game, gets one icon that marks it as unknown. A class that names no icon gets the plain entity icon.

## Playing does not change the level

Entities only run in play mode. They work on a copy of your settings and wires, so a counter that counted to 6 while you played is back at its start value when you stop. A door that slid open is back where you built it.

A wire with a limited number of fires is the same: the count runs down while you play and is whole again afterwards.

## Watching the wiring

The Logic view draws a level's entities as cards and its wires as lines between them, and lights each wire up as it fires while you play. <kbd>Ctrl</kbd> + <kbd>L</kbd> opens it. See [See and wire a level's logic](/guides/see-and-wire-a-levels-logic/).

The console can print every output that fires and every input that arrives while you play. Type `ent_watch on` in the editor's Console panel, then press Play. A wire aimed at a name nothing has prints a `miss` line. The <kbd>&#96;</kbd> key opens the panel while the level plays.

The console also lists a level's entities, shows one entity's wires and state, and sends an input by hand. See [Console](/reference/console/).

## Classes the engine doesn't know

If a level uses a class this build doesn't have, the entity is kept exactly as written, settings and wires included. It does nothing when you play, and the editor marks it.

The classes that exist today are listed in [Logic entities](/reference/logic-entities/), [Mover entities](/reference/mover-entities/), [Trigger entities](/reference/trigger-entities/), [Sound entities](/reference/sound-entities/) and [Player entities](/reference/player-entities/).
