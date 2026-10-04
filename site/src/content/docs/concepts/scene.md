---
title: The scene
description: Everything in a level is a node in one tree.
---

Everything in a level lives in one tree, the scene. Each item in the tree is a node.

A node has:

- a name
- a position, a rotation and a scale, measured from its parent
- any number of children
- an id that never changes, so undo, saved levels and wiring keep pointing at the same node

## What a node can carry

A node with nothing on it is a group. It turns into something by carrying one of these:

| It carries | So it is |
|---|---|
| A brush | A block, a part or a cut: geometry you build in the editor. See [Blocks, parts and cuts](/concepts/blocks-parts-and-cuts/). |
| A mesh | A model from a file, such as a prop. |
| A light | A sun, a point light, a spot light or an area light. |
| An entity | Logic that runs while you play. See [Entities and wiring](/concepts/entities-and-wiring/). |

The scene tree in the editor shows the kind next to each name.

## Parents and children

A child moves with its parent. Group a few things, move or rotate the group, and everything inside follows.

There is one limit. You can't scale a block, part or cut, or a group that holds one. Brushes are resized by changing their shape, with the size tool.

## Names

Names don't have to be unique. Wiring works by name, and a wire aimed at a name reaches every node called that.
