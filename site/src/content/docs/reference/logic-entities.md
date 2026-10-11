---
title: Logic entities
description: The entity classes that ship with the engine.
---

These classes have no shape. They exist to be wired together. See [Entities and wiring](/concepts/entities-and-wiring/) for how wires work.

The classes with a shape are on [Mover entities](/reference/mover-entities/) and [Trigger entities](/reference/trigger-entities/). The sound is on [Sound entities](/reference/sound-entities/) and the player's start on [Player entities](/reference/player-entities/).

In each settings table, the first column is the name a level file uses and the second is the label in the Properties panel.

Takes is the parameter an input reads, and Sends is the value an output sends in the parameter's place. See [What an input takes](/concepts/entities-and-wiring/#what-an-input-takes).

## logic_relay

Passes a trigger on, and can be switched off.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `startdisabled` | Start disabled | 0 | Set to 1 and the relay ignores `Trigger` until it gets `Enable`. |

| Input | Takes | Does |
|---|---|---|
| `Trigger` | Nothing | Fires `OnTrigger`, if the relay is enabled. |
| `Enable` | Nothing | Switches the relay on. |
| `Disable` | Nothing | Switches the relay off. |
| `Toggle` | Nothing | Switches it the other way. |

| Output | Sends | Fires when |
|---|---|---|
| `OnTrigger` | Nothing | A `Trigger` got through. |

## logic_timer

Fires on a fixed interval.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `startdisabled` | Start disabled | 0 | Set to 1 and the timer waits for `Enable`. |
| `refiretime` | Refire time | 1 | Seconds between fires, rounded up to whole ticks. Anything below 0.01 counts as 0.01, which is one tick. |

| Input | Takes | Does |
|---|---|---|
| `Enable` | Nothing | Starts the timer with a whole interval ahead. On a running timer it starts the interval over. |
| `Disable` | Nothing | Stops it. |
| `Toggle` | Nothing | Starts it if stopped, stops it if running. |
| `ResetTimer` | Nothing | Starts the interval over without firing. Does nothing on a stopped timer. |
| `FireTimer` | Nothing | Fires now and leaves the schedule alone. |
| `RefireTime` | A number | Sets the interval to the parameter, from the next interval on. |

| Output | Sends | Fires when |
|---|---|---|
| `OnTimer` | Nothing | An interval ends. |

## math_counter

Holds a number, with an optional floor and ceiling.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `startvalue` | Start value | 0 | The number it starts at. |
| `min` | Minimum | 0 | The floor. |
| `max` | Maximum | 0 | The ceiling. |

The floor and ceiling only apply when `max` is above `min`. With both left at 0 the counter has no limits.

| Input | Takes | Does |
|---|---|---|
| `Add` | A number, or nothing | Adds the parameter, or 1 if there is none. |
| `Subtract` | A number, or nothing | Subtracts the parameter, or 1 if there is none. |
| `SetValue` | A number | Sets the number to the parameter. |
| `SetValueNoFire` | A number | Sets the number without firing any output. |
| `SetHitMax` | A number | Changes the ceiling. Fires nothing. |
| `SetHitMin` | A number | Changes the floor. Fires nothing. |
| `GetValue` | Nothing | Fires `OutValue` with the current number. |

| Output | Sends | Fires when |
|---|---|---|
| `OutValue` | A number | The number changes, or `GetValue` asks for it. It carries the number. |
| `OnHitMax` | Nothing | The number arrives at the ceiling. |
| `OnHitMin` | Nothing | The number arrives at the floor. |

## logic_auto

Fires once when the level starts. It has no settings and takes no inputs.

| Output | Sends | Fires when |
|---|---|---|
| `OnMapSpawn` | Nothing | The level starts, once every entity is ready. |

## logic_branch

Holds a true or false value.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `initialvalue` | Initial value | 0 | The value it starts with: 0 for false, 1 for true. |

Setting the value fires nothing. The outputs fire only when the branch is tested.

| Input | Takes | Does |
|---|---|---|
| `SetValue` | `0` or `1` | Sets the value to the parameter, 0 or 1. |
| `SetValueTest` | `0` or `1` | Sets the value, then tests it. |
| `Toggle` | Nothing | Flips the value. |
| `ToggleTest` | Nothing | Flips the value, then tests it. |
| `Test` | Nothing | Fires `OnTrue` or `OnFalse` for the current value. |

| Output | Sends | Fires when |
|---|---|---|
| `OnTrue` | Nothing | A test finds the value true. |
| `OnFalse` | Nothing | A test finds the value false. |

## logic_case

Compares a value against up to 16 cases and fires the output of the one it matches.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `case01` to `case16` | Case 01 to Case 16 | empty | The value that fires the output with the same number. An empty case is not used. |

Numbers match as numbers, so `3` and `3.0` are the same case. Anything else matches as text, and capitals count. If two cases hold the same value, the lower number wins.

| Input | Takes | Does |
|---|---|---|
| `InValue` | Text | Compares the parameter against the cases. |

| Output | Sends | Fires when |
|---|---|---|
| `OnCase01` to `OnCase16` | Nothing | The value matches that case. |
| `OnDefault` | Text | The value matches no case. It carries the value. |

For more than 16 cases, wire `OnDefault` to the `InValue` of a second `logic_case`.

## logic_compare

Holds a number and compares it against another.

| Setting | In the editor | Default | Meaning |
|---|---|---|---|
| `initialvalue` | Initial value | 0 | The number it starts with. |
| `comparevalue` | Compare value | 0 | The number to compare against. |

Setting either number fires nothing. The outputs fire only on a comparison.

| Input | Takes | Does |
|---|---|---|
| `SetValue` | A number | Sets the number. |
| `SetValueCompare` | A number | Sets the number, then compares. |
| `SetCompareValue` | A number | Sets the number to compare against. |
| `Compare` | Nothing | Compares the two numbers now. |

| Output | Sends | Fires when |
|---|---|---|
| `OnLessThan` | Nothing | The number is below the compare value. |
| `OnEqualTo` | Nothing | The two numbers are the same. |
| `OnNotEqualTo` | Nothing | The two numbers differ. It fires before `OnLessThan` or `OnGreaterThan`. |
| `OnGreaterThan` | Nothing | The number is above the compare value. |

Equal means the same number, with no rounding. Whole numbers are safe. A number built by adding fractions, like 0.1 ten times, can miss its target, so test those with `OnLessThan` or `OnGreaterThan`.
