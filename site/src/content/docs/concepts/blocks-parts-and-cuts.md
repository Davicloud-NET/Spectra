---
title: Blocks, parts and cuts
description: The three kinds of brush and when to use each.
---

Level geometry is built from brushes: simple solid shapes you place and resize. A brush is one of three kinds.

## Block

A block is solid geometry that merges with the level. Where two blocks overlap they become one solid, and the faces hidden between them are gone.

Use blocks for the world itself: floors, walls, stairs, pillars.

Insert one with <kbd>Ctrl</kbd> + <kbd>1</kbd>.

## Part

A part stands on its own and never merges. Two parts that overlap just pass through each other.

Moving a part costs nothing, where moving a block makes the engine rebuild the geometry around it. So use parts for anything that moves, and for anything you place many times.

Insert one with <kbd>Ctrl</kbd> + <kbd>2</kbd>. The editor outlines parts in cyan.

## Cut

A cut removes solid from the blocks it overlaps. That is how you make a doorway, a window or a tunnel.

A cut draws nothing itself. The editor outlines it in magenta so you can find it. A new cut starts half-buried in the surface you pointed at, because a cut that only touches a surface removes nothing.

Cuts only affect blocks. A part is never cut.

Insert one with <kbd>Ctrl</kbd> + <kbd>3</kbd>.

## Switching kind

<kbd>Ctrl</kbd> + <kbd>T</kbd> converts the selection between block and part.

## Resizing

The size tool (<kbd>3</kbd> or <kbd>R</kbd>) puts a handle on every face. Drag one and that face moves while the opposite face stays where it is. Hold <kbd>Shift</kbd> to resize around the centre instead.

Size snaps in world units, so one step is the same distance on a small brush and a large one.

## Materials

Every face of a brush has its own material. A face with no material is plain grey.

## No sealed levels

A level does not have to be closed. There is no leak check and no map boundary, and building far from the origin costs the same as building at it.
