---
title: Coming from Roblox
description: What works the way you know it from Roblox Studio, what differs in the first hour, and what is not there yet.
---

If you have made games in Roblox Studio, you know most of this engine's scripting already. The language is Luau, the real one, with `--!strict`, `continue`, `+=` and string interpolation. A level is a tree of nodes, a script sits in the tree, and `script.Parent` is the node above it.

This page lists what differs. Read the first part before you write a line, and come back to the tables when something surprises you.

## What you can keep

These are here under the names you know. Most do what they do in Roblox. Where one does not, its line says so, and the rest of the page has the detail.

- `script`, `script.Parent`, `workspace`, `game`, `game:GetService`.
- `workspace.Door` for a child by its name. A member wins over a child of the same name.
- `FindFirstChild` and its relatives, `WaitForChild`, `GetChildren`, `GetDescendants`, `IsA`, `ClassName`, `Name`, `Parent`, `GetFullName`.
- `Instance.new`, which makes a part, a folder or a light and nothing else. `Clone` and `Destroy`.
- `Position` and `CFrame`, both in the world. The `CFrame` maths is Roblox's own.
- `Vector3` and `Color3`.
- Signals: `Connect`, `Once`, `Wait`, and `Disconnect` on the connection. Connected functions are called in another order than Studio calls them.
- `task.wait`, `task.spawn`, `task.defer`, `task.delay`, `task.cancel`. The old `wait`, `spawn` and `delay` too. A wait counts whole ticks.
- `RunService.Heartbeat` and `Stepped`. They fire on the level's tick, and not once a frame.
- `Players.LocalPlayer` and `CollectionService`.
- Attributes and tags: `SetAttribute`, `GetAttribute`, `AddTag`, `HasTag`, `GetTagged`.
- `print`, `warn`, `error`, `pcall`, `typeof`, and Luau's own libraries.
- The words of an error. A name a node does not have fails with `Anchored is not a valid member of Part "Wall"`, and never gives a silent `nil`.

## The first hour

### Scripts are files

A script in the level is a node that names a `.luau` file in the project's `Assets` folder. Insert Script makes the file and opens it in Visual Studio Code, where the Luau extension gives you completion and type checking. The editor has no script editor of its own.

A file is read when you press Play. Save a change while the level plays and nothing happens until you stop and play again.

[Write your first script](/guides/write-your-first-script/) sets all of it up.

### One kind of script

There is `Script`, and a `Script` with Module switched on. There is no `LocalScript`, no server and no client, and one player. A script runs once when the level starts, wherever it is in the tree.

A module is usually a file another script asks for by its path: `require("./lib/score")`. [Make things while the level plays](/guides/make-things-while-the-level-plays/#share-a-module) has the path rules.

`_G` is read only and there is no `shared`. Two scripts share state through a module, an attribute or a tag.

### The level is the tree

`workspace` is the root of the level, and everything is under it. There is no `ReplicatedStorage`, `ServerStorage`, `ServerScriptService`, `StarterGui` or `Lighting`. A template you clone from sits in the level.

`game:GetService` knows four names: `RunService`, `Workspace`, `Players` and `CollectionService`. Any other is an error at that line: `'TweenService' is not a valid Service name`.

### Blocks and parts

World geometry here is built from brushes, and a brush is a block, a part or a cut. A block merges with the level: overlapping blocks become one solid. A part stands alone, as a Roblox part does. A cut removes solid from blocks, which is `NegateOperation` without the union step.

A script works with parts. It cannot move, clone, re-parent or destroy a block, and it cannot hear one being touched. Each is an error that says to convert the block to a part, which is one button in the editor.

Nothing falls. There are no unanchored parts yet, so there is no `Anchored`: a part stays where it is until something moves it.

A script cannot set a part's size, colour, material or transparency yet. It can set `CanCollide`, `CanQuery`, `CanTouch` and `Visible`, which draws or hides a part.

[Blocks, parts and cuts](/concepts/blocks-parts-and-cuts/) has more.

### A child moves with its parent

`Position` and `CFrame` are where a node is in the world, as in Roblox. But a node here follows the node above it. Move or turn a part, and every part, light and group under it goes along.

`LocalPosition` and `LocalCFrame` are a node's place under its parent.

So there is no `Model`, no `PrimaryPart`, no `PivotTo` and no welding. To move several things as one, put them under one node. A group is a node with nothing else on it, and `Instance.new("Folder")` makes one.

A node that gets another `Parent` keeps its place in the world, as a Roblox part does.

### A unit is a metre

Roblox counts in studs, and a stud is about 0.28 metres. Here one unit is one metre, and the player is 1.8 units tall. So a number copied from a Roblox script is three to four times too big.

| | Roblox's default | The same in metres | The default here |
|---|---|---|---|
| `WalkSpeed` | 16 | about 4.5 | 4.5 |
| `JumpHeight` | 7.2 | about 2 | 1.2 |

The player walks as fast as in Roblox and jumps lower.

A new `Part` is 4 × 1.2 × 2 studs in Roblox and a cube of 1 unit here. A light with a `Range` of 16 reaches 16 metres, which is about 57 studs.

Angles are radians wherever Roblox uses radians.

### The tick

`Heartbeat` and `Stepped` fire on the level's tick: 60 times a second, each time with the same step, whatever the frame rate. A frame can hold no tick, one, or several. In Roblox they fire once a frame.

`task.wait` counts in whole ticks. `task.wait()` is one tick and `task.wait(0.04)` is three.

There is no `RenderStepped` yet, and a script cannot reach the camera.

[Move a part with a script](/guides/move-a-part-with-a-script/) shows all of it.

### Signals wait their turn

A connected function never runs in the middle of the call that fired its signal. It is put in the level's queue and runs when its turn comes. Roblox calls this deferred, and a place with deferred signals does the same.

The order is what differs. Functions connected to one signal are called in the order they connected, and Studio calls the one connected last first.

An entity's input waits its turn too. `door:Open()` puts the input in the queue, so the door has not moved on the next line.

### The player

The player is `Players.LocalPlayer.Character`. It is not a model and not in `workspace`, and it has no `Humanoid`, no `HumanoidRootPart` and no health.

There is one player, and the character is there from a script's first line. So there is no `PlayerAdded` and no `CharacterAdded`, and the lines that open most Roblox scripts, `Players.PlayerAdded:Connect` and `player.CharacterAdded:Wait()`, are errors here. Leave them out and read `Players.LocalPlayer.Character`.

- `character.Position`, `character.CFrame` and `character.Velocity` say where it is and how it moves.
- `character.WalkSpeed` and `character.JumpHeight` are yours to set.
- `character:Teleport(place)` moves it and `character:Respawn()` sends it back to the start.

`part.Touched` hands a function the character, not a part of it. So the oldest line in Roblox scripting, `hit.Parent:FindFirstChild("Humanoid")`, does not port. Only the player touches things.

How the player walks is a function you can replace. See [Change how the player moves](/guides/change-how-the-player-moves/).

[Make a kill brick and checkpoints](/guides/kill-brick-and-checkpoints/) is the place to start.

### Which way is forward

`CFrame` maths is Roblox's, so a frame's `LookVector` is its negative Z.

The engine's own things face the other way. A light shines along its positive Z, and a player start faces along its positive Z. So `CFrame.lookAt` aims a spot light away from its target. Turn it round:

`CFrame.lookAt(light.Position, target) * CFrame.Angles(0, math.pi, 0)`

`character:Teleport(frame)` faces the player along the frame's `LookVector`. Hand it a player start's own `CFrame` and the player looks the wrong way, for the same reason.

### Stop puts everything back

Stop ends every script and undoes all they did: what they moved, renamed, made, destroyed, tagged and set, and the player's speed. The level you edit is the level you built, so a play never has to be undone.

Nothing carries over to the next Play. Every script starts from its first line.

### Entities and wires

A door, a button, a trigger, a timer and a sound are entities: nodes with settings, inputs and outputs. Wiring an output to an input is how a level does things with no code at all.

To a script an input is a method and an output is a signal. `door:Open()` sends a door its `Open` input, and `button.OnPressed:Connect` takes a function to call when the button is pressed.

A setting reads through `GetAttribute`. What a script sends and hears shows beside the wires in the editor's Logic view. [Open a door three ways](/guides/talk-to-the-level/) builds one door with a wire, with a counter and with a script, and says when to use which. [Hear what the level does](/guides/hear-what-the-level-does/) listens to it.

A Roblox `ProximityPrompt` is a Button entity here. A `Sound` is a Sound entity, and a script plays one through its `Play` input.

## The rest

| | In Roblox | Here |
|---|---|---|
| Functions connected to one signal | Studio calls the one connected last first. The documentation promises no order. | Called in the order they connected. |
| A value of the wrong type written to a property | Often taken: `light.Enabled = 1` switches the light on. | An error at that line. |
| A light's `Range` | 0 and less are taken as 0, and more than 120 as 120. | 0 and less are an error. There is no upper bound. |
| A light's `Brightness` | Roblox's own scale. | The light's colour is multiplied by it. A number from a Roblox place is a first guess. |
| `CanQuery` | Counts only while `CanCollide` is off. | Decides alone whether the engine's own rays find a part, such as the one the player presses a button with. |
| A light | A child that lights from where its part is. | A node with a place of its own. There are also `DirectionalLight` and `DiscLight`, and a `SurfaceLight` is a rectangle that shines. |
| `Transparency` | Hides a part at 1. | `Visible` draws or hides a part. |
| An attribute | Can hold an `Instance`. | Cannot. It holds a string, a number, a boolean, a `Vector3`, a `CFrame` or a `Color3`. |
| An attribute's name | May have `.`, `-` and `/` in it. | Letters, digits and `_` only. |
| `GetTags` | Sorted by name. | In the order the node got its tags. |
| The tags and attributes of a destroyed node | Kept, and `AddTag` and `SetAttribute` still work. | Gone. `HasTag` is false, `GetAttribute` is `nil`, and `AddTag` and `SetAttribute` are errors. |
| `Parent` set to a destroyed node | Taken. | An error. |
| `WaitForChild` with a timeout, on a destroyed node | `nil` when the time is over. | `nil` at once. |
| A signal a script kept of a node that was destroyed since | `Connect` gives a live connection. | `Connect` gives a connection that is never called, and `Wait` is an error. |
| `Color3.fromHSV` | A hue outside 0 to 1 gives numbers that are no colour. Fewer than three numbers are an error. | The hue goes round. A number left out is 0. |
| `_G` | A table every script can write to. | Read only. |
| `tick`, `time`, `elapsedTime`, `Random` | There. | Not there. `os.clock`, `os.time` and `math.random` are. |
| `Enum`, `Vector2`, `UDim2`, `TweenInfo` | There. | Not there. |
| A loop that never waits | The script is ended after a while. | The script is stopped after three seconds and switched off until the next Play. |
| `print` | No bound. | All scripts together print 100 lines a tick. The rest are dropped and one warning says how many. Warnings and errors always get through. |
| The root of the level | `Workspace`, of class `Workspace`. | `workspace` is of class `Folder` and has the name of the level's root node. |
| The type checker | Knows the place's tree. | Does not know your level. `workspace.Door` is some node to it, until the script says `:: func_door`. |

## What is not there yet

A script cannot do these today. One that reaches for a service, a class or a member that is not there gets an error that names it, and never a silent `nil`.

A global is the exception. `Enum`, `Vector2`, `tick` and the others in the table above are `nil`, like any name nobody set. So `Vector2.new(1, 2)` fails with Luau's own words, `attempt to index nil with 'new'`, and `local kinds = Enum` fails nowhere. Visual Studio Code marks each as an unknown global before you play.

- Read keys or the mouse. There is no `UserInputService`. The player's mover is handed walk, look, jump, sprint, crouch and use.
- Run once a frame, or reach the camera: `RenderStepped`, `workspace.CurrentCamera`.
- Put anything on the screen: a `ScreenGui`, a label, a button.
- Tween. There is no `TweenService`. Move things on `Heartbeat`.
- Let a part fall, or hear two parts touch. Only the player collides and touches.
- Cast a ray: `workspace:Raycast`.
- Set a part's `Size`, `Color`, `Material` or `Transparency`.
- Hear the tree change: `ChildAdded`, `DescendantAdded`, `Changed`, `GetPropertyChangedSignal`.
- Fire a signal of its own. There is no `BindableEvent`. A module that keeps a list of functions does the job.
- Play an animation, or move a `Humanoid`.
- Save anything: `DataStoreService`.
- Talk between a server and a client. There is one player and no `RemoteEvent`.
- Write an entity class in Luau, with inputs and outputs of its own.
- Run in the console. There is no command bar for Luau.
- Stop at a breakpoint. There is no debugger, so `print` it is.
- Run while you edit, as a plugin does.
- Run as native code or in parallel: `--!native`, `Actor`.

## Where to go next

1. [Write your first script](/guides/write-your-first-script/)
2. [Move a part with a script](/guides/move-a-part-with-a-script/)
3. [Open a door three ways](/guides/talk-to-the-level/)
4. [Hear what the level does](/guides/hear-what-the-level-does/)
5. [Make a kill brick and checkpoints](/guides/kill-brick-and-checkpoints/)
6. [Make things while the level plays](/guides/make-things-while-the-level-plays/)
7. [Change how the player moves](/guides/change-how-the-player-moves/)

The [script reference](/reference/script/) lists every class, member and global a script can reach.
