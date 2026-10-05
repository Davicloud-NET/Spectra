---
title: Player entities
description: The entity class that says where the player starts.
---

The player is not an entity, and no wire can name them. The one class here says where they start.

## info_player_start

Where the player starts. It has no settings, takes no inputs and fires no outputs. The engine reads where the node is and which way it points.

The player stands on the node and faces along its local +Z. The editor draws the node as a green diamond with an arrow for that direction.

A level uses the first player start in the scene tree. A player who falls out of the level is put back there.

A level with no player start still plays. The player starts at the level's default spot and the log warns once.
