---
title: Logic entities
description: The entity classes that ship with the engine.
---

These classes have no shape. They exist to be wired together. See [Entities and wiring](/concepts/entities-and-wiring/) for how wires work.

## logic_relay

Passes a trigger on, and can be switched off.

| Setting | Default | Meaning |
|---|---|---|
| `startdisabled` | 0 | Set to 1 and the relay ignores `Trigger` until it gets `Enable`. |

| Input | Does |
|---|---|
| `Trigger` | Fires `OnTrigger`, if the relay is enabled. |
| `Enable` | Switches the relay on. |
| `Disable` | Switches the relay off. |
| `Toggle` | Switches it the other way. |

| Output | Fires when |
|---|---|
| `OnTrigger` | A `Trigger` got through. |

## logic_timer

Fires on a fixed interval.

| Setting | Default | Meaning |
|---|---|---|
| `startdisabled` | 0 | Set to 1 and the timer waits for `Enable`. |
| `refiretime` | 1 | Seconds between fires. |

| Input | Does |
|---|---|
| `Enable` | Starts the timer. |
| `Disable` | Stops it. |
| `Toggle` | Starts it if stopped, stops it if running. |
| `ResetTimer` | Starts the interval over without firing. Does nothing on a stopped timer. |
| `FireTimer` | Fires now and leaves the schedule alone. |
| `RefireTime` | Sets the interval to the parameter, from the next interval on. |

| Output | Fires when |
|---|---|
| `OnTimer` | An interval ends. |

## math_counter

Holds a number, with an optional floor and ceiling.

| Setting | Default | Meaning |
|---|---|---|
| `startvalue` | 0 | The number it starts at. |
| `min` | 0 | The floor. |
| `max` | 0 | The ceiling. |

The floor and ceiling only apply when `max` is above `min`. With both left at 0 the counter has no limits.

| Input | Does |
|---|---|
| `Add` | Adds the parameter, or 1 if there is none. |
| `Subtract` | Subtracts the parameter, or 1 if there is none. |
| `SetValue` | Sets the number to the parameter. |
| `SetValueNoFire` | Sets the number without firing any output. |
| `SetHitMax` | Changes the ceiling. |
| `SetHitMin` | Changes the floor. |
| `GetValue` | Fires `OutValue` with the current number. |

| Output | Fires when |
|---|---|
| `OutValue` | The number changes, or `GetValue` asks for it. It carries the number. |
| `OnHitMax` | The number arrives at the ceiling. |
| `OnHitMin` | The number arrives at the floor. |

## logic_auto

Fires once when the level starts. It has no settings and takes no inputs.

| Output | Fires when |
|---|---|
| `OnMapSpawn` | The level starts, once every entity is ready. |

## logic_branch

Holds a true or false value.

| Setting | Default | Meaning |
|---|---|---|
| `initialvalue` | 0 | The value it starts with: 0 for false, 1 for true. |

Setting the value fires nothing. The outputs fire only when the branch is tested.

| Input | Does |
|---|---|
| `SetValue` | Sets the value to the parameter, 0 or 1. |
| `SetValueTest` | Sets the value, then tests it. |
| `Toggle` | Flips the value. |
| `ToggleTest` | Flips the value, then tests it. |
| `Test` | Fires `OnTrue` or `OnFalse` for the current value. |

| Output | Fires when |
|---|---|
| `OnTrue` | A test finds the value true. |
| `OnFalse` | A test finds the value false. |

## logic_case

Compares a value against up to 16 cases and fires the output of the one it matches.

| Setting | Default | Meaning |
|---|---|---|
| `case01` to `case16` | empty | The value that fires the output with the same number. An empty case is not used. |

Numbers match as numbers, so `3` and `3.0` are the same case. Anything else matches as text, and capitals count. If two cases hold the same value, the lower number wins.

| Input | Does |
|---|---|
| `InValue` | Compares the parameter against the cases. |

| Output | Fires when |
|---|---|
| `OnCase01` to `OnCase16` | The value matches that case. |
| `OnDefault` | The value matches no case. It carries the value. |

For more than 16 cases, wire `OnDefault` to the `InValue` of a second `logic_case`.

## logic_compare

Holds a number and compares it against another.

| Setting | Default | Meaning |
|---|---|---|
| `initialvalue` | 0 | The number it starts with. |
| `comparevalue` | 0 | The number to compare against. |

Setting either number fires nothing. The outputs fire only on a comparison.

| Input | Does |
|---|---|
| `SetValue` | Sets the number. |
| `SetValueCompare` | Sets the number, then compares. |
| `SetCompareValue` | Sets the number to compare against. |
| `Compare` | Compares the two numbers now. |

| Output | Fires when |
|---|---|
| `OnLessThan` | The number is below the compare value. |
| `OnEqualTo` | The two numbers are the same. |
| `OnNotEqualTo` | The two numbers differ. It fires before `OnLessThan` or `OnGreaterThan`. |
| `OnGreaterThan` | The number is above the compare value. |

Equal means the same number, with no rounding. Whole numbers are safe. A number built by adding fractions, like 0.1 ten times, can miss its target, so test those with `OnLessThan` or `OnGreaterThan`.
