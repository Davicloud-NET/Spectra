---
title: Units and axes
description: How big a unit is, which way is up, and what the grid snaps to.
---

## Units

One unit is one metre. The character you walk around as is 1.8 units tall.

## Axes

- X is right
- Y is up
- A camera with no rotation looks along negative Z

The floor grid in the editor sits at Y = 0.

## Snapping

| Tool | Snaps to | Default | Steps |
|---|---|---|---|
| Move | world units | 1 | 0.25, 0.5, 1, 2, 4 |
| Size | world units | 1 | 0.25, 0.5, 1, 2, 4 |
| Rotate | degrees | 15 | 5, 15, 45, 90 |

<kbd>G</kbd> turns snapping on and off. <kbd>[</kbd> and <kbd>]</kbd> step to a finer or coarser grid. Hold <kbd>Alt</kbd> while dragging to flip snapping for that one drag.

## Rotation

The properties panel shows rotation as three angles in degrees. Level files store it as a quaternion.
