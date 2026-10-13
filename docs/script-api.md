# Adding to the script API

Everything a script can reach is declared once, in C#, by attributes on the code that implements it. A generator in `SpectraEngine.Entities.Generator` (`ScriptApi/`) reads the attributes and writes the binding. The same declarations become data, and the definitions file for `luau-lsp` and the reference are written from that data. So the three cannot say different things.

The attributes and the data are in `SpectraEngine.Core/Scripting/Api`. The declarations are in `SpectraEngine.Scripting.Luau/Runtime`.

## Declare it

A class carries one of four attributes. It must be `partial`, since the generator writes its other half.

| Attribute | Declares | The class |
|---|---|---|
| `[ScriptClass("Instance")]` | objects a script holds | static, and every member takes the object first. Or not static and derived from `ScriptObject`, and the members are its own |
| `[ScriptValue("CFrame")]` | a type a script holds by value | static, and every member takes the value first |
| `[ScriptLibrary("task")]` | a global table | static or not |
| `[ScriptGlobals]` | globals | static or not |

A member carries one attribute, and its `///` summary is what a person reads about it in the editor and in the reference. A member with no summary is an error.

A property. The object is the first parameter:

```csharp
/// <summary>The node's class: its entity class when it has one, otherwise its kind.</summary>
[ScriptProperty]
public static string ClassName(SceneNode node) => NodeClassName.Of(node);
```

A method. Its parameters and what it returns are its own. A parameter with a default, or one that can be null, may be left out by a script. A tuple is several results:

```csharp
/// <summary>Whether the other frame is this one, to within epsilon.</summary>
[ScriptMethod]
public static bool FuzzyEq(CFrame self, CFrame other, float epsilon = 1e-5f) => self.FuzzyEq(other, epsilon);
```

A signal, on the property that gives it. The text says what a listener is handed:

```csharp
/// <summary>Fires right after PostSimulation, with the step in seconds.</summary>
[ScriptSignal("deltaTime: number")]
public ScriptSignal Heartbeat { get; }
```

A property or a signal of a static class may take the `ScriptContext` in front of the object. A static class has no fields, and the context is where it finds what the level keeps, such as the one signal object a part has for a touch:

```csharp
/// <summary>Fires once when the player starts to touch the part.</summary>
[ScriptSignal("character: Character")]
public static ScriptSignal Touched(ScriptContext context, SceneNode node) =>
    Touch(context, node, TouchSignal.Touched);
```

A property a script can set cannot: the generator writes its body, and that takes the node alone.

A function of a library, and a constant of one:

```csharp
/// <summary>A rotation by an angle in radians about an axis.</summary>
[ScriptFunction(Name = "fromAxisAngle")]
public static CFrame FromAxisAngle(Vector3 axis, float angle) => CFrame.FromAxisAngle(axis, angle);

/// <summary>The frame at the origin with no rotation.</summary>
[ScriptProperty(Name = "identity")]
public static CFrame Identity => CFrame.Identity;
```

A global value and a global function, in a class with `[ScriptGlobals]`. `[ScriptDeprecated]` marks an old spelling:

```csharp
/// <summary>The root of the level's tree.</summary>
[ScriptProperty(Name = "workspace")]
public SceneNode Workspace { get; }

/// <summary>Waits so many seconds. Returns the seconds that passed and the level's time.</summary>
[ScriptFunction(Name = "wait", Parameters = "seconds: number?", Returns = "(number, number)")]
[ScriptDeprecated("task.wait")]
private int Wait(LuauStack stack) => tasks.OldWait(stack);
```

`Name` is only needed when a script's name is not the member's own. Every name a script reads has to be one Luau can write, and so has the name of a C# parameter, which the description takes as it is. A word Luau keeps, such as `end` or `then`, is an error.

### A class that extends another

`Extends = "Instance"` on `[ScriptClass]` gives a class every member of another. Its switches hand a name the class does not declare to the class it extends, so a script finds there what the description promises. Both must be static classes whose members take the same object, such as a `SceneNode`. Anything else is an error. A class with `TypeParameters` extends nothing and has no operators.

A class may declare no member at all, as `Part` and `DirectionalLight` do today:

```csharp
/// <summary>A light that shines one way everywhere, like the sun.</summary>
[ScriptClass("DirectionalLight", Extends = "Light")]
internal static partial class DirectionalLightApi;
```

It still has the three switches. The catalogue writes them, since only it knows the type of the object, and each hands the name to the class it extends. Its first member moves the switches into its own file, and nothing else has to change.

### A class of nodes

A node is asked through the class of what it carries. `NodeClass.KindOf` in Core says which that is, and `NodeMembers` calls that class's switch. So a member declared on `LightApi` is found on every light and on no part. An entity counts as what carries it: a door is asked as a `Part`.

Core also says what a class extends (`NodeClass.BaseOf`), which is what `IsA` goes by. The script API says it again with `Extends`, and a test holds the two alike.

A new class of nodes is added in five places: a `NodeClassKind`, its name and what it extends in `NodeClass`, the class with `[ScriptClass]`, a line in each switch of `NodeMembers`, and a node in `ClassHolders` with its line in the description test.

No node is a `BasePart` or a `Light` and nothing more. Such a class has no kind, no line in `NodeMembers` and no holder. It is named in `NeverAlone` in the description test, and its members are asked of the classes that extend it.

A name that is no member of a node is the name of a child: `workspace.Door`. `ObjectMembers` looks for one only when the switches have answered that there is no such member, so a member always wins and costs what it did. `ChildType` on `[ScriptClass]` tells the type checker. Only `Instance` says it, and the definitions file writes it on every class that extends `Instance`: Luau's checker finds a property on the classes a class extends, and an indexer on the class alone (`external/luau/Analysis`).

### What crosses

| C# | Luau |
|---|---|
| `float`, `double`, `int` | `number` |
| `bool` | `boolean` |
| `string` | `string`. As a parameter it makes a string on every call |
| `ReadOnlySpan<byte>` | `string`, as a parameter only. It is read where Luau keeps it and makes nothing |
| `Vector3` | `Vector3` |
| `CFrame` | `CFrame` |
| `Color3` | `Color3` |
| `SceneNode` | `Instance` |
| a class with `[ScriptClass]` | that class |

A new kind of value is added in three places: `ScriptTypeMap` and `ScriptMarshalling` in the generator, and a reader in `ScriptArguments`. One a script holds in a userdata of its own, as it holds a `CFrame` and a `Color3`, also gets a tag in `ScriptTags`, a class that pushes and reads it (`ColorValue`), and a metatable set up by hand (`Color3Members`).

A `Color3` is a colour as a person picks one, and the engine lights in linear values. What stores a linear colour crosses at its own door: a light through `Light.DisplayColor`, a keyvalue through `Color3.FromLinear`.

### A property a script can set

A script changes a node through `ScriptWrites` and nothing else, so Stop can put it back. `ScriptWrites` keeps its own list, since Core cannot see the binding. A settable property names the door's member and has no body:

```csharp
/// <summary>Where the node is in the world.</summary>
[ScriptProperty(Sets = SettableMemberId.Position)]
public static partial Vector3 Position(SceneNode node);
```

The generator writes the body, which reads the same member of the door. So what a script reads is what it wrote. The member itself is added to `ScriptWrites` first: a `SettableMemberId`, a row in its list, a case in `Read` and in `TryWrite`, and a `JournalKind`.

The door takes a string, a number, a boolean, a `Vector3`, a `CFrame` or a `Color3`, and `ScriptContext` has a writer for each. It also takes a node that may be none, which is what `Parent` is: such a property is declared as `SceneNode?`. A new kind is a `MemberValueKind`, a writer there, and a case in the two switches of `ScriptClassEmitter`.

A member not every node has says so in its row: `Classes` names the script classes that have it, as `Range` names the four lights with a range. `Read` then gives no value for a node of another class, and the property is declared on each class the row names. The class is the one `NodeClass.KindOf` gives, so a part that carries a light has the members of a part and none of a light. A write the node cannot take is refused in the door, with words that say what to do: a switch of a world brush says to convert the brush to a part. A write of the value the member already has records nothing.

What is changed in place has to be seen by whoever draws or simulates it. A light raises no signal: the renderer reads every light again on each frame. The mover, the touch tracker and a ray read a part's switches when they run. The touch tracker reads `CanTouch` on a brush an entity senses with, and on a part or a mesh a script listens to.

A write a script can make every tick must not build anything each time. A part that is not drawn holds no GPU mesh, so a write of `Visible` first tells the scene to keep the part's mesh (`Scene.KeepMeshWhileHidden`), and the journal lets go of it at Stop. A part that blinks then builds its mesh once.

A value that is a number can make one that is none further on. The renderer gets a light's colour times its brightness, so the door refuses a write that takes that past a float.

A property that cannot be set answers "Property is read only". `CannotSet` gives it other words. The definitions file marks it `read`, so the type checker refuses the write the binding would refuse.

### A property of an object that is no node

The door sets nodes. An object of a class that is not static takes a write itself: `SetBy` names a method of the class that gets the value and returns null, or why it refused the value. The script reads that as its error.

```csharp
/// <summary>How fast the player walks, in units a second. A unit is a metre, so Roblox's 16 is about 4.5 here. Stop puts back what a script set.</summary>
[ScriptProperty(SetBy = nameof(SetWalkSpeed))]
public float WalkSpeed => _door.WalkSpeed ?? throw Left();

private string? SetWalkSpeed(float value) => _door.TrySetWalkSpeed(value, out string? refused) ? null : refused;
```

The value is a number, a boolean, a string, a `Vector3`, a `CFrame` or a `Color3`. One of another kind is refused before the method is called, in the words the door uses.

What the method changes has to go back at Stop as well. The player's members go through `PlayerWrites` in Core, which records in the journal the door records in. A new one is a method there, and a `TuningJournalKind` when it changes the tuning, which outlives the level. The tuning is read through `PlayerWrites` too: `IPlayerPresence` does not hand it out, so no entity can write it past the journal.

`PlayerWrites` refuses what is no number or out of range, and nothing else. It does not ask whether a place is free. A teleport into something solid is taken, as `trigger_teleport` takes it, and the mover pushes the player out through the nearest face over the next ticks. With the feet just under a floor that is up, so a teleport to a point in the floor stands the player on it. With most of the player under the floor it is down and out of the level, where the fall-out guard starts the player again. `TeleportIntoSolidTests` holds each case.

### Written by hand

Some members cannot be generated: `CFrame.new` takes six shapes of arguments, `task.wait` yields. Such a member takes a `LuauStack` and returns how many values it pushed, as every host function does. Its attribute says what a script may hand it, as Luau writes it:

```csharp
/// <summary>Calls the function every time the signal fires, with what it was fired with.</summary>
[ScriptMethod(Parameters = "callback: (T...) -> ()", Returns = "RBXScriptConnection")]
private int Connect(LuauStack stack) => Listen(stack, "Connect", isOnce: false);
```

In a static class the object comes after the stack: `static int GetComponents(LuauStack stack, CFrame self)`. Several `[ScriptOverload]` attributes declare several shapes.

A member written by hand may take the `ScriptContext` in front of the stack. A static class has no fields of its own, and the context is where it finds what the level keeps for its scripts:

```csharp
[ScriptMethod(Returns = "{Instance}")]
private static int GetChildren(ScriptContext context, LuauStack stack, SceneNode node)
```

A property and a signal of a static class may take the context as well, in front of the object. That is how a signal of a node finds the object that stands for it:

```csharp
[ScriptSignal("attribute: string")]
public static ScriptSignal AttributeChanged(ScriptContext context, SceneNode node) =>
    context.Attributes.ChangedSignal(node);
```

A property with `Sets` takes the node and nothing else, since the generator writes its body.

### A signal and who fires it

A signal is a `ScriptSignal` some object hands out, the same one on every read. It is fired in one of three ways:

- Where the host stands, with `ScriptSignal.Fire`. `RunService` does: its listeners run at that point of the tick.
- From the level's queue, for an output of an entity. `NodeSignals` keeps those listeners in the level's own table.
- Through `DeferredCalls.Fire`, for what a script sets off itself: `AttributeChanged` and the two signals of a tag. Every listener the signal has at that moment gets a call in the level's queue (`EntityWorld.QueueScriptCall`) and is called when it comes due, unless it was disconnected by then. So nothing runs inside the call that fired. A call a tick's budget drops, or the queue has no room for, is a fire that listener does not hear. The level's log then speaks of a listener's call, and not of a thread that waits.

### What scripts leave on nodes

Tags and the attributes scripts set are kept in the runtime and on no node: `TagTable` and `AttributeTable`. So they need no journal, and Stop takes them away with the runtime. The two tables fire nothing and know nothing of Luau. `LevelTags` and `AttributeMethods` stand in front of them and fire the signals.

A name no script has set reads the keyvalue of the node's entity (`AttributeRead`). What a script sets under that name hides the keyvalue from scripts and never reaches the entity. `GetAttributes` hands the keyvalues out as they read, so it can hold what `SetAttribute` refuses: a node for a keyvalue that refers to one, and a name no attribute has.

Two values are the same when they are of one kind and equal. So 0 and -0 are one value, as they are for a node's own members: a set of one over the other writes nothing and fires nothing, and the one set first stays.

A tag's list holds the nodes that are in the level's tree. It is put into the tree's order when it is asked for after a node joined it or a node moved in the tree (`TreeOrder`), and is handed over as it is every other time. The sort walks up from each node and looks over the children where the paths part, which in a level with every part under the root is most of the level. A node that loses the tag stays in the list until it is asked for next, and all that left are then taken out in one pass.

Neither table is in the memory Luau counts for the scripts, so `MemoryBudget` does not see them. They have bounds of their own, `AttributeLimits` and `TagLimits` in Core: an attribute's string is at most 65536 bytes long, a node holds at most 256 attributes and keeps the signals of at most 256 names, a tag is at most 256 bytes long and a node has at most 256 tags. A script that passes one gets an error that names it. Whoever puts a level's own tags and attributes into the tables goes past these checks, and has to hold them to the same numbers.

Nothing is kept for what cannot be: `GetAttributeChangedSignal` hands out one signal that never fires for every name no attribute can have. A name whose last attribute is gone and a tag no node has stay for a while, so that one which comes and goes is made once. They are let go when there are twice as many as were left the last time (`AttributeTable.NameOf`, `LevelTags.LookOver`). A tag stays past that while a node has it, a listener waits on one of its signals, or a script still holds one. Luau says which signals are held, after a full collection, which the look-over runs when it has to ask.

### What a script makes, clones, moves and destroys

A script changes the level's tree through `TreeWrites` in Core, which `ScriptWrites.Tree` hands out, and through nothing else. `Instance.new` is `TryMake`, `Clone` is `TryClone` and `Destroy` is `TryDestroy`. `Parent` is a member of the door like any other, and its write comes to `TrySetParent`.

- Before a parent's list of children changes, the play journal keeps the list as it is, once (`TreeJournal`). Stop puts every kept list back and moves only a node that is out of place. So a world brush never leaves the scene, and nothing recompiles.
- A node a script made carries a mark (`TreeWrites.IsMade`). What a level changes on it records nothing, since the node is gone at Stop. A destroyed node carries another (`TreeWrites.IsDestroyed`) until Stop.
- A node keeps its place in the world when its parent changes, as a Roblox part does. A light does too: here it is a node with a place of its own, where a Roblox light is wherever its part is. Every node of a destroyed tree reads the place it stood at, so its local transform is recorded and Stop puts it back.
- `MadeNodes` says which classes `Instance.new` makes and what a new node of each is. `TreeLimits` bounds how many made nodes there are at a time and how deep a tree goes.
- An entity at or below a node that comes into the level is queued for the end of the tick (`EntityWorld.QueueSpawn`), and one whose node leaves is queued for its removal. Entities that come together are all built, named and wired before the first of them spawns, as those of the level are.
- An entity whose node gets another parent inside the level runs on. What it read when it spawned may no longer hold, so it is told: `Entity.OnOwnedBrushesChanged` when a script put a brush under it or took one away, which a trigger and a door answer by collecting again. A `LinearMover` takes its two poses again when its node has another parent, from where the node is and how far along it is.
- A clone keeps every name of its source, and a wire finds its target by name. So `TryClone` gives the nodes of one clone a number (`SceneNode.CopyGroup`), and `TargetNameIndex.Resolve` sends what an entity of a clone names to the entities of that clone, when one of them has the name. A wire from anywhere else reaches every entity of the name, the clones too. The Logic view draws a clone's wires by the same rule (`LogicEntityInfo.CopyGroup`).

`NodeLifetime` in the binding does what the runtime owes a node:

- A script at or below a node that comes into the level starts at the end of the tick, after the entities (`IScriptRuntime.StartEntered`). One whose node leaves is switched off, for a while or for good, and starts afresh when the node comes back.
- For a destroyed node, `Forget` ends the connections to what it fires (`NodeSignals.Forget`), ends the waits on it (`ChildWaits.Destroyed`), lets go of the module it is (`ModuleLoader.Forget`) and takes its tags and attributes away. Whatever else the runtime comes to keep by node needs a line there, or it is kept for every part a level ever spawned.
- A script that kept a signal of a node finds it dead after the node was destroyed: `Connect` gives a connection that is never called and the level lists no listener, and `Wait` is an error, since nothing would resume the thread. `WaitForChild` on a destroyed node gives nil with a timeout and ends the thread with a warning without one, whether it began before the node was destroyed or after. `require` of a destroyed module is an error.
- A made node that is in no level is listed in `LooseNodes`. When there are twice as many as were left the last time, and when the bound on made nodes is reached, Luau collects in full, and a tree none of whose nodes a script still holds stops counting.
- What a script holds of a destroyed node keeps the whole node, which the memory Luau counts does not see. So a destroyed node a script made counts against the bound for as long as a script holds it (`HeldNodes`). It stops counting when Luau has collected the handle in its own time, which is looked for each time there are twice as many, and a full collection is asked for only when the bound is reached. `TreeWrites.MadeCount` can therefore be above what is in the level until `LuauScriptRuntime.Collect` has run.
- `LuauObjects.Reached` tells the runtime when a script comes to hold an object none held. A made node that had stopped counting counts again then: the level's queue can hand a script a node after the last script let go of it, as a call of `InstanceRemoved` does.
- A clone gets the tags and attributes of its source from the two tables (`TagTable.Copy`, `AttributeTable.Copy`). What seeds a level's own into the tables is copied with them.

Ending a thread can run a script, which can come back into `NodeLifetime`. So a call there takes a list of its own and never walks one that another call may change.

Two more things stay by hand:

- An operator. `[ScriptOperator("*", "Vector3", "Vector3")]` goes on the method that implements it, and that method is put into the metatable by hand (`CFrameMembers`).
- What is set outside the script API's own setup: `script`, which each script gets for itself, and `warn`, which a host has before it has a level. `ByHand = true` on `[ScriptProperty]` or `[ScriptFunction]` declares it without binding it.

### A global the type checker has too

`require` is declared like any global function with by-hand arguments (`ModuleLoader`), so the reference has it with its summary. The definitions file leaves it out. The type checker has a `require` of its own, which follows a path to its file and gives the module its type. A declaration in the file would take its place, and every module would be `any`. `LuauDefinitions.IsTheCheckersOwn` holds the names that are left out this way.

## The members of an entity class

An entity class is data, so no attribute declares what a script finds on its node. Its schema does. An input is a method and an output is a signal:

```lua
local button = workspace.Button :: func_button
local door = workspace.Door :: func_door

button.OnPressed:Connect(function()
    door:Open()
end)
```

`EntityClassApi` in `SpectraEngine.Core/Scripting/Api` is the one place that reads a schema this way. `MembersOf` gives the inputs and outputs a script reaches, and `Describe` writes them as a class. The binding (`EntityMembers`) answers a script from `MembersOf`, and the definitions file and the JSON are written from `Describe`, so the three say the same.

A build's own description knows no level. `LuauScriptApi.Description.WithEntityClasses(schemas)` adds the classes of a set of schemas, and that is what a host hands to `LuauDefinitions.Write` and `ScriptApiJson.Write`. The demo's switches hand it the built-in classes, read back from `.sentdef` bytes.

A class Luau cannot name as a type is left out of the description, and so is an input or an output that is no member. `EntityClassGaps.Of` gives a sentence for each, and whoever writes the files logs them, as the demo's switches do.

Who wins a name: a member of the node's own class, then an input, then an output, then a child. A name Luau cannot write after a colon is no member.

The node's own class is the class of what it carries, and the binding asks by that: `EntityClassApi.MembersOf` takes the carrier. The description cannot know a node. It goes by where the class is placed, `Part` for a class placed as a brush and `Instance` for any other. The two differ when a class sits on a node of another kind, as a point class on a light, and a member of that node then wins where the file says method. So `EntityClassGapsTests` holds every input and output of the built-in classes free of the members of every class of nodes. A project's class has no such test. The first time a script asks a node of it, the level's scripts are warned of each input and output they cannot reach there.

What a value is to a script goes by the type the schema gives the input or the output (`EntityClassApi.KindOf`):

| The schema says | A script hands over, or is handed |
|---|---|
| no value | nothing |
| `Bool` | a boolean |
| `Int` | a number with no fraction |
| `Float` | a number |
| `String`, `TargetName` | a string |
| `Vec3`, `Color`, `Angles` | a `Vector3` |
| `Vec2`, `Vec4` | two or four numbers, each a value of its own |
| `NodeRef` | a node, or nil |
| nothing, as a `.sentdef` from before the types | one value of any of these kinds in, the wire's text out |

`ParameterText` turns the value into the text a wire carries and back, so an entity reads from a script what it reads from a wire. `WireValues` in the binding is the Luau side of that.

The description says what the binding takes. For the last row that is `(boolean | number | string | Vector3 | Instance)?` in and `string?` out, from `ValuesOf` and `SentValuesOf`. An output fired with text its type cannot read calls its listeners with no value, and the scripts are warned the first time an output does.

Neither runs inside the call. `door:Open()` queues the input, and a fired output queues one event for each listener (`EntityWorld.Listeners`), numbered on from the output's own wires. Both wait with what scripts made, so they take the scripts' share of a tick and of the queue.

Scripts start on the first tick. What fired while the level spawned is past by then, as the `OnMapSpawn` of a `logic_auto` is: a wire carries it and no script hears it. The first script that connects to such an output, or waits for it, gets a warning at that line.

A listener ends with `Disconnect` and with the level. One whose script was switched off, or whose thread was cancelled, ends when its output fires next. `NodeSignals` also looks all listeners over each time there are twice as many, so those do not pile up on an output that never fires again.

The type checker cannot know a level. `workspace.Door` is an `Instance` to it, and a script says what it is with a cast, as above.

## The touch of a part

`Touched` and `TouchEnded` are signals of `BasePart`, declared like any other. The level's queue fires them, as it fires an output:

```lua
local lava = workspace.Lava :: Part

lava.Touched:Connect(function(character)
    character:Respawn()
end)
```

A listener sits in `EntityWorld.Listeners` under the node and one of the two names of `TouchSignal`. The touch pass (`TouchTracker`, with `PartTouches` for what scripts listen to) looks there once a tick:

- Only the player touches. The listener is handed the character, the same object `Players.LocalPlayer.Character` gives.
- A level where no script listens for a touch pays one check a tick. Otherwise the pass asks the scene for what is near the player and measures only a node that has a listener, or a touch that has yet to end.
- The mover stops the player one skin off a solid part and counts as contact what is within two. So a solid part is touched within two skins, and the skin is the one the player says (`IPlayerPresence.SkinWidth`). A part with `CanCollide` off is touched by overlap, as a trigger is.
- What the mover says of its last tick is touched whatever the gap: the part the player stands on (`CharacterState.GroundNodeId`), and each part its slide stopped at (`CharacterState.SlideContacts`). Off a corner the slide ends the tick well clear of what it hit, the further the faster the player is, so the end of the tick alone would miss it.
- A mesh has no shape of its own yet and is touched by overlap with the box around it, stretched as the node is scaled. A baked map does not load a mesh yet. Nothing touches one there, and the level's log warns once for each such mesh a script listens to (`TouchSignal.WhyNotHere`).
- A touch starts once and ends once, when the player is clear by `TouchTracker.ExitMargin`, when `CanTouch` goes off, when the node leaves the scene or when the player leaves the level. Stop tells nobody. A listener of an end that came because the player left is handed the character all the same, so a script can tell whose touch ended. Reading a member of it is an error by then, as for any object that has left.
- A part first listened to with the player on it is touched on the next tick. A touch outlives its listeners: one that comes while it goes on hears no second start, so a thread that calls `Touched:Wait()` in a loop is resumed once for a touch.
- A trigger entity is told first, as before, and fires its own outputs then. What a script hears of the same touch is queued after them. Among parts, ends go before starts, each by the oldest listener a part has at that time.
- A world brush cannot tell what touches it. The fused world is hulls that name no node, in an authored level and in a baked one, so `Connect`, `Once` and `Wait` on its signal are an error that says to convert the brush to a part (`TouchSignal.WhyNone`).

A touch is no entity's output, so a trace is told nothing of it yet.

## What typeof says

`typeof` reads its answer off a userdata's metatable, and a userdata tag has one metatable. So a signal and a connection each have a tag of their own (`ScriptTags`), with the metatable every object has but for its `__type`: `RBXScriptSignal` and `RBXScriptConnection`, as in Roblox. Every other object is an `Instance`. `ScriptContext.PushObject` picks the tag by the object's type, and a reader that takes any object tries all three (`ScriptTags.TryGet`).

`StudioAnswerTests` holds answers of Roblox Studio that the engine gives too. A test there names the answer it states.

## What is generated

For a class or a value, in the other half of the declaring class:

- `ScriptRead`, `ScriptWrite` and `ScriptInvoke`: a read, a write and a call of a member, each a switch on the member's atom. A write of a settable property calls the door. Every other write answers that the member is read only. A name the class does not declare goes to the class it extends, through `ScriptBases` in the catalogue.
- one method for each typed method, which checks every argument, converts it, calls and pushes the result. A wrong argument is refused in Roblox's words: `invalid argument #2 to 'Lerp' (number expected, got string)`.
- `ScriptMethods`: the functions a script gets when it reads a method and does not call it.
- `ScriptDescription`: the class as data.

For a library and for globals: `ScriptInstall`, which makes the table or sets the globals, and the description.

One more file, `ScriptApiCatalog.g.cs`, numbers every member name (`ScriptAtoms`) and every class (`ScriptClassIds`), and gathers the descriptions. A name's number is its atom: Luau hands it over in place of the name, so `part:FindFirstChild(...)` reaches its code through `__namecall` and a switch on a number, with no string compared.

The catalogue also holds the switches of every class that declares no member, as above.

The metatables are still set up by hand, in `ObjectMembers`, `CFrameMembers`, `Vector3Members` and `Color3Members`. Each finds the object, calls the generated switch, and puts a name that means nothing into the words a script reads. For a node, `ObjectMembers` goes through `NodeMembers`, which picks the class.

A bad declaration is an error, `SPS001` to `SPS010`. `SpectraEngine.Entities.Generator/AnalyzerReleases.Unshipped.md` lists them. One about what an attribute says points at the attribute. One about the member itself points at the member: no summary, a type that cannot cross, a missing object parameter, a shape that cannot be called. A parameter Luau cannot name is marked at the parameter, and a class that extends what it cannot is marked at the class.

## What the tests then demand

- `ScriptApiDescriptionTests` asks an object of every class for every name, and compares with the description in both directions. A new class needs an object in its `Holders`. The object has to be of that class and of none below it: a `Part` as the holder of `Instance` would answer the members of `Part` too. A member that a metatable answers by hand and no attribute declares fails here.
- `NodeClassScriptTests` reads `NodeMembers` and fails when a class of nodes is sent to another script class than the one of its name.
- `SettableListTests` holds the settable properties of the description equal to the list of `ScriptWrites`, by class, name, type and member.
- `SettableMemberClassTests` in `SpectraEngine.Bsp.Tests` holds the classes a row names equal to the nodes the member reads a value on.
- `StopPromiseTests` writes to every settable member and compares the level's saved bytes after Stop. Between the writes it makes, clones, moves and destroys nodes, from C# (`RandomTree`) and from scripts (`WritingScript`). A new kind of value needs a case in `RandomWrites`.
- `SpawnCostTests` fails when a level that spawns and destroys a part on every tick still holds one of the destroyed parts, anywhere in the engine or the runtime. Its table of times runs when asked for, like the ones below.
- `SpawnKeepsNothingTests` holds the same for a clone that carries a script, a module its script asks for, and an entity. Whatever else a clone can carry that the runtime keeps something for gets a test there.
- `HeldNodeLimitTests` holds the bound on made nodes against a script that destroys what it makes and keeps it.
- `PlayerSettingTests` sets every settable property of the character from a script and compares what outlives a play session after Stop. It also tries every name on the player's objects: what takes a value must be what the description says a script can set.
- `LuauDefinitionsTests` checks that the definitions file has every class, member and global, and that it has no `require`.
- `ScriptSampleTests` fails when no clean sample script names a class or uses a global. See [Checked by luau-lsp](#checked-by-luau-lsp).
- `TouchCostTests` fails when a tick with parts listened to allocates. Its table of times runs when asked for, like the one below.
- `ScriptCostTests` fails when a crossing allocates. A new kind of crossing gets a row there. Its table of times is a measurement, which runs only when asked for: `dotnet run -c Release --project Test/SpectraEngine.Scripting.Tests -- -explicit only -class "SpectraEngine.Scripting.Tests.ScriptCostTests"`.
- The generator's own tests are in `Test/SpectraEngine.Entities.Tests` (`ScriptApiGenerator*`). They compile its output against stubs of the binding in `ScriptApiHarness`. When the generator calls something new in the binding, the stub gets it too.
- `EntityClassApiTests` holds the built-in entity classes against the script API: no input or output may be named as a member of the node that carries it, and each has to be in the definitions file.
- `EntityClassGapsTests` holds the same names free on every class of nodes, so a new member of `Light` or `BasePart` that a built-in class has as an input fails there.
- `EntityClassBindingTests` asks a node of an entity class for every name of its schema, and compares with the description of the class.

## The files

The demo writes both and opens no window. Neither is committed.

```bash
dotnet run --project SpectraEngine.Executable -- --export-script-types=spectra.d.luau
dotnet run --project SpectraEngine.Executable -- --export-script-api=script-api.json
```

`LuauDefinitions.Write` and `ScriptApiJson.Write` are in Core and need no Luau. `LuauScriptApi.Description` is the data, and reading it loads no native library.

The syntax of a definitions file has no specification. `luau-lsp` 1.70.1 loads this one under both of its type solvers, entity classes and all, and its `analyze` command reports a wrong call on a node that a script cast to its class. Every construct is written in one place in `LuauDefinitions`, so a correction is one line there and one in `LuauDefinitionsTests`.

## In a project

The editor makes the same text as `--export-script-types` (`ScriptDefinitions`, and a test compares the two calls). When a script of a project opens, `ScriptLauncher` first has `ScriptProjectSetup` write four files beside the project's `Assets`:

| File | Written |
|---|---|
| `spectra.d.luau` | when it is missing, and again whenever its text is not this build's. A text that differs only in its line ends is left alone. One behind a byte order mark is written again without it, since luau-lsp does not read the file with one |
| `.luaurc` | when it is missing. It holds the alias `assets` for `./Assets` and nothing else |
| `.vscode/settings.json` | when it is missing. The platform is `standard`, the definitions file is named as `@spectra`, and the Rojo sourcemap is off |
| `.vscode/extensions.json` | when it is missing. It recommends `JohnnyMorganz.luau-lsp` |

The last three belong to the person once they exist. They are never written over and never merged into, also when one is not valid JSON. Their text is in `ScriptProjectFiles`.

Opening a project brings an old `spectra.d.luau` up to the build and writes nothing else. A project that never opened a script gets no file. A folder that does not take a file is one warning in Output, and the script opens all the same.

## Checked by luau-lsp

`Test/SpectraEngine.Scripting.Tests/Samples/Assets` is the `Assets` folder of a small project. `LuauLspTests` sets a project up in a temporary folder with the code above, copies the samples in, and runs `luau-lsp analyze` on them under the old type solver and under the new one. Its arguments are read from the `.vscode/settings.json` that was written.

- A script under `Scripts` is clean: luau-lsp has to say nothing of it. The file Insert Script writes and the demo's `Assets/Scripts/spinner.luau` are checked with them.
- A script under `Mistakes` has one mistake a line. Each such line ends in an arrow and the words luau-lsp has to say of it, and no other line may be reported:

```lua
door.Position = 5 --> 'Vector3', but got 'number'
frame.Position = Vector3.zero --> new: Position of type 'CFrame' is read-only
print(frame + frame) --> old: 'Vector3', but got 'CFrame' --> new: Operator '+' could not be applied
```

Words after `old:` or `new:` are for that solver alone, and the other one then has to say nothing of the line. A sample says `--!strict` on its first line, or it is checked the way a new project is, which finds far fewer mistakes.

A new class or global needs a line in a clean sample, or `ScriptSampleTests` fails and names it. A global that is marked as old cannot be called in a clean sample, so it needs a line in a mistake that ends in the checker's words for it. A new member is worth a line of each kind: one that uses it, and one that hands it the wrong type.

The tests that start luau-lsp carry the trait `Suite=LuauLsp`. They run only where `SPECTRA_LUAU_LSP` holds the path of the program, and skip everywhere else. CI fetches release 1.70.1 by its hash (`.github/actions/luau-lsp`), keeps the zip in its cache, and fails when one skipped.

```bash
SPECTRA_LUAU_LSP=/path/to/luau-lsp dotnet run --project Test/SpectraEngine.Scripting.Tests -- -trait "Suite=LuauLsp"
```

What luau-lsp cannot check is in `AGENTS.md`: a write to a `read` property passes under the old solver, and a name it does not know is a child of the node.

The scripts on the site's page [Write your first script](../site/src/content/docs/guides/write-your-first-script.mdx) are samples too. `FirstScriptGuideTests` compares each block of the page with its sample and plays the samples in a level.
