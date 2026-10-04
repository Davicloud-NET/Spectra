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
