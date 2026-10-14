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

The tags and attributes scripts work with are kept in the runtime: `TagTable` and `AttributeTable`. Nothing a script does to them reaches a node. So they need no journal, and Stop takes them away with the runtime. The two tables fire nothing and know nothing of Luau. `LevelTags` and `AttributeMethods` stand in front of them and fire the signals.

A level starts with what it was built with. A builder gives a node tags and attributes in the editor (`SceneNode.Tags` and `SceneNode.Attributes`, `docs/data-model.md` §3), and `LevelSeed` puts them into the two tables at the head of the first tick, before the first script starts. So `GetTagged`, `HasTag`, `GetAttribute` and `GetAttributes` answer with them from a script's first line, and no signal fires for them. From then on they are the play session's copy: a script takes a built tag off or sets a built attribute as it does its own, and the node keeps what it was built with. A node that comes into the level later is read when it comes, once, and its tags fire the added signal as any tag of an arriving node does.

On a node that carries an entity a keyvalue wins. An attribute a node was built with under the name of one of its entity's keyvalues is not put into the table: scripts read the keyvalue, which is what the entity runs with, and the level warns once for each such attribute. The editor refuses to make one (`PropertyLimits.TakenByEntity`), so it takes a hand-written map or an entity put on the node afterwards.

A name no script has set reads the keyvalue of the node's entity (`AttributeRead`). What a script sets under that name hides the keyvalue from scripts and never reaches the entity. `GetAttributes` hands the keyvalues out as they read, so it can hold what `SetAttribute` refuses: a node for a keyvalue that refers to one, and a name no attribute has.

Two values are the same when they are of one kind and equal. So 0 and -0 are one value, as they are for a node's own members: a set of one over the other writes nothing and fires nothing, and the one set first stays.

An attribute's name keeps one rule, `AttributeName` in Core, which is Roblox's as Roblox Studio answers it: 1 to 100 letters, digits and any of `_ . - /`, with no `RBX` in front. `SetAttribute`, the editor's panel and a map all ask it, so a name a script sets is one a level can be built with. A name that breaks it is an error with the rule's sentence, and `GetAttribute` of one is nil.

`GetTags` hands a node's tags out sorted, on the node and on `CollectionService`, as Roblox does. The order is `TagName.CompareBytes` in Core: by the bytes of their UTF-8, the shorter first when one tag begins the other. So a digit comes before a capital letter and a capital before a small one. `TagMethods` sorts when it is asked. The table keeps the order the node got its tags in, which is the order a map and the panel have.

A tag's list holds the nodes that are in the level's tree. It is put into the tree's order when it is asked for after a node joined it or a node moved in the tree (`TreeOrder`), and is handed over as it is every other time. The sort walks up from each node and looks over the children where the paths part, which in a level with every part under the root is most of the level. A node that loses the tag leaves its place in the list empty: `TagNodes.InLevel` knows each node's place, and `TagNodes.Vacant` stands there from then on. The empty places are taken out in one pass when the list is asked for next. So taking a tag off every node of a large tag is still paid once, and the list keeps no node that left it, destroyed or not. Knowing the places takes 7 bytes more for each tagged node of the level than the set of them took.

What scripts set counts against `MemoryBudget` by its bytes. `AttributeTable` and `TagTable` each keep the sum for the nodes that are not destroyed, in the level or out of it, and change it with every set and every removal. `NodeBytes` adds the two and hands the sum to the shim, which counts it with Luau's own memory. The estimate is `NodeWeight`'s: an attribute takes 64 bytes for its place, 32 and two for each character of its name, and text longer than 28 bytes an array's 24 and its bytes. A tag takes 256 and two for each character. A value or a tag the node was built with counts for nothing. It is the level's: the node keeps it whatever scripts do, and the table only refers to it. So a level built with long attributes leaves its scripts the whole budget, and a script that takes a built value off, or sets it again, moves nothing. Long text counts unless it is the array the node was built with, so equal text a script handed over counts. What scripts set is no garbage while the node lives, so Luau's collector is not told of it, and the full collection the budget check makes takes none of it away. A made tree in no level is the exception: one no script holds goes when `LooseNodes` looks the trees over, and a collection does not do that. So the shim also keeps about what loose trees hold (`HostCells`). When a userdata was collected since the last look-over, some of those trees may be held by no script, and the interrupt does not stop a script that only their bytes put past the budget. It asks for a look-over, which comes at the end of the tick, and what is still held then counts in full again. The sum goes down when an attribute or a tag is taken away, when a node is destroyed (what a destroyed node keeps is weighed as the part on destroyed nodes says), when a loose tree is let go of, and to nothing at Stop.

A `SetAttribute` or `AddTag` that adds bytes asks whether the scripts are still under the budget. When they are not, Luau collects in full and the loose trees no script holds are let go of first. Past the budget the value or the tag is not kept, nothing fires, and the script is switched off: "the scripts hold more than 256 MB, with the tags and attributes they set on nodes, and this one was switched off". The stop is the one the interrupt makes (`ScriptMemory.Fail`), so a pcall does not hold on. A `Clone` whose copies go past the budget makes nothing and stops the script the same way. What went over is not kept, so the level stays under the budget and its other scripts run on. A set that adds nothing, as a number set over a number, costs a few additions and no call into the shim.

They also have bounds of their own, `AttributeLimits` and `TagLimits` in Core: an attribute's string is at most 65536 bytes long, a node holds at most 256 attributes and keeps the signals of at most 256 names, a tag is at most 256 bytes long and a node has at most 256 tags. A script that passes one gets an error that names it. `LevelSeed` puts a level's own tags and attributes into the tables past these checks. `TagSet` and `AttributeSet` hold a node to the same numbers when it is built, so a node built full takes no more from a script. A destroyed node is held to them too. One a script made counts against `TreeLimits.MaxMadeNodes` for as long as a script holds it, and what it keeps counts against `MemoryBudget`, which the part on destroyed nodes below says more of.

A node whose last attribute was taken away, and that has no signal a script asked for, is kept nothing for (`AttributeTable.Forget`). A node's first attribute takes about 125 bytes, a second 72 more, and each one after that 48 (`AttributeBytesTests`). A value is 40 bytes, its kinds sharing them (`MemberValue`). A node keeps its names in slots it walks, the first in `NodeAttributes` itself and the rest in an array just long enough, and in a dictionary past 32 names (`NodeAttributes.MostWalked`). The array is smaller than a dictionary at every count. Finding the last of 32 names takes about 130 ns of `GetAttribute`, where a dictionary takes about 105, and a name in the middle takes as long as in a dictionary. The slots keep the order a dictionary would. `GetAttributes` fills its table in that order, which decides what `pairs` reads first where two names collide in the table.

Text is kept as the bytes of UTF-8 a script handed over and handed back as the same bytes, with no .NET string between (`MemberValue.OfUtf8`, `LuauStack.PushUtf8`). Text of up to 28 bytes is kept in the value, and longer text in an array of its own length, so even a 64 KiB string stays under the 85000 bytes that put an object on .NET's large object heap. Setting text equal to what is held keeps what is held and allocates nothing. Bytes that are not valid UTF-8 are refused, since a map writes the text as a string and could not hold them. A builder's text is a string in the editor and a map, and `AttributeSet` keeps it as UTF-8 too (`MemberValue.AsUtf8`). Two texts are equal by what they say, whichever form they are in.

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
- For a destroyed node, `Destroyed` ends the connections to what it fires (`NodeSignals.Forget`), ends the waits on it (`ChildWaits.Destroyed`) and lets go of the module it is (`ModuleLoader.Forget`). Whatever else the runtime comes to keep by node needs a line in `EndAllOf`, or it is kept for every part a level ever spawned.
- A destroyed node keeps its tags and attributes, as in Roblox. `HasTag`, `GetTags`, `GetAttribute` and `GetAttributes` answer as they did, and `AddTag`, `RemoveTag` and `SetAttribute` are taken with no error. It is in no tag's list, so `GetTagged` leaves it out. Nothing it has fires a signal: the listeners of its `AttributeChanged` are ended, and what a script asks for afterwards is the one signal that never fires.
- The two tables take a destroyed node out of the nodes they count and hold it weakly (`TagTable.Destroyed`, `AttributeTable.Destroyed`). So what it keeps lasts as long as the node itself: while a script holds it, while the level's queue has yet to hand it to a listener, while a tag's list still names it, and until Stop for a node the level was built with, since the play journal keeps that one. A script holds a node until Luau has collected its handle. Once the handle is collected and nothing else has the node, the node and what it kept go together. Stop clears both tables, the destroyed nodes too.
- A tree no script can reach any more is forgotten whole, with its tags and attributes (`Forget`, which `LooseNodes` calls).
- A script that kept a signal of a node finds it dead after the node was destroyed: `Connect` gives a connection that is never called and the level lists no listener, and `Wait` is an error, since nothing would resume the thread. `WaitForChild` on a destroyed node gives nil with a timeout and ends the thread with a warning without one, whether it began before the node was destroyed or after. `require` of a destroyed module is an error.
- A made node that is in no level is listed in `LooseNodes`. A look-over collects in full and forgets every tree none of whose nodes a script still holds, and the tree stops counting. It comes when there are twice as many trees as were left the last time, when the bound on made nodes is reached, and once scripts have added to loose trees more than the larger of `HeldNodes.FirstGoal` and an eighth of what Luau holds, or than half the room left in the budget when that is less. A tree adds what it keeps to that the first time it leaves the level, so a script that hides a made model and shows it again asks for no collection. A full collection takes time for each byte Luau holds, so what this costs for a byte added is the same at any size of heap, and what waits is at most that share of it. The step every 64 ticks that the part below speaks of is also taken while the trees a look-over left keep more than the first goal. When such a step ends a cycle, Luau has said which of their nodes no script holds, and those trees are let go of with no collection of their own. So a script that lets them go and makes nothing more is found.
- What a script holds of a destroyed node keeps the whole node, which the memory Luau counts does not see. So a destroyed node a script made counts against the bound for as long as a script holds it (`HeldNodes`). `LuauObjects.Released` tells the runtime when Luau has collected the handle, and the node stops counting and leaves `HeldNodes` then. The collected handles are read at the end of every tick, before the table of objects grows, and when a cycle of the collector ends that `HeldNodes` asked for. A full collection is asked for only when the bound is reached. `TreeWrites.MadeCount` can therefore be above what is in the level until `LuauScriptRuntime.Collect` has run.
- Luau's collector paces itself by the bytes it counts, and of a node it counts a handle of 24 bytes. So it is told what the node weighs. `NodeWeight` estimates that: 1024 bytes for the node, 128 for what its attributes are kept in, 64 for each attribute, 256 for each tag, two for each character of an attribute's name and of a tag, and for an attribute's text longer than 28 bytes an array's 24 bytes and its bytes. A tag is weighed as one no node of the level has, which the destroyed node keeps alone. `LuauObjects.SetWeight` hands the number to the shim, which keeps it beside the handle and takes it off when the collector frees the handle. A destroyed node a script holds is weighed when it is destroyed, when a script gives it a tag or an attribute, and when it gets a new handle: Luau can collect the handle before the node's place in `LuauObjects` is let go, and the level's queue can hand the node out in between.
- Under `HeldNodes.FirstGoal`, 256 KiB of such weight, nothing is asked of the collector. Past the goal, every byte a destroyed node adds has the collector work as it does for a byte a script allocates (`LuauHost.StepCollector`), until its cycle ends. The collector steps by whole kilobytes, so a smaller gain is owed until there is one. The goal is then twice what was still held, so a script that holds much is not collected again for every part. A script that lets destroyed nodes go and destroys nothing more is found by a step every 64 ticks, taken while more than the first goal is held. That step leaves the goal in force: what is destroyed under the goal while its cycle runs steps nothing. `docs/performance.md` has what this costs and how many parts wait.
- The weight counts against `MemoryBudget` with what Luau counts itself. The check is the one Luau's own memory has: a full collection, and the script that is running is switched off when the two together are still over. Its error names the larger of what destroyed nodes keep and what scripts set on nodes. So a script that keeps handles to destroyed nodes with long strings is stopped at the budget, where the bound on made nodes would have let it keep 16384 of them.
- The estimate counts a text once for every node that has it, also where a clone shares the string with its source. It counts nothing for a destroyed node of the level as it was built, which the play journal keeps until Stop whatever Luau does. What scripts set on a made node in no level counts against the budget as it does on any node that is not destroyed, and the line on `LooseNodes` says when such a node is let go of.
- `LuauObjects.Reached` tells the runtime when a script comes to hold an object none held, also one whose handle Luau collected while the object kept its place. It can step the collector, so `Released` can run under it. A made node that had stopped counting counts again then: the level's queue can hand a script a node after the last script let go of it, as a call of `InstanceRemoved` does.
- A clone gets the tags and attributes of its source from the two tables (`TagTable.Copy`, `AttributeTable.Copy`). What seeds a level's own into the tables is copied with them. `LevelSeed` does not read the copy when it comes into the level, so it keeps what scripts set on its source.

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
- `SpawnKeepsNothingTests` holds the same for a clone that carries a script, a module its script asks for, and an entity, and for one whose tags and attributes a script reads and sets after it destroyed the clone. Whatever else a clone can carry that the runtime keeps something for gets a test there.
- `HeldBytesTests` holds what destroyed parts keep in bytes, with no collection asked for: a loop that destroys parts with a long string keeps few waiting, a script that keeps them is switched off at the memory budget and told why, one that holds them is not collected for each part, and one that lets them go is found with no other Destroy. A part handed out again under a new handle is weighed again, and tags and names count with their text. `HeldBytesPacingTests` holds when the collector steps: not for a gain under a kilobyte, and not for the step every 64 ticks. `HeldBytesCostTests` writes the numbers down when asked for.
- `NodeBytesTests` holds what scripts set on nodes that are not destroyed against the budget: a script that sets long attributes or many tags past it is switched off with the sentence, also from inside a pcall, a clone past it is not made and the message counts the copy, and the other scripts run on. The count goes down when an attribute or a tag is taken away and when a node is destroyed, is nothing after Stop, and counts nothing a node was built with. A seeded random run of sets, removals, tags, moves, clones and destroys holds the count to a recount from every node after each tick.
- `LooseBytesTests` holds that loose parts with long attributes a script lets go of are looked for by bytes, in one call and on every tick, with no collection asked for, that those a script keeps keep their attributes, that those it lets go of are found with no other part made, and that a script that keeps them past the budget is switched off. What one script let go of does not stop another that builds up Luau data before a look-over, what a script still holds stops one past the budget after it, and a model hidden and shown on every tick asks for no collection.
- `HeldNodeLimitTests` holds the bound on made nodes against a script that destroys what it makes and keeps it, with a tag and an attribute on each. It also holds that room is found at the bound once the script lets them go, while it keeps loose nodes too.
- `DestroyedNodeKeepsTests` holds what a destroyed node keeps: for a script that holds it, for a listener the level's queue hands it to after every script let go, and no longer than the node. One case asks for no collection, so a part goes when Luau collects it by itself, one asks a tag's list that named the parts, and one never asks it again. It also plays a level twice, so nothing a destroyed node took is there after Stop.
- `PlayerSettingTests` sets every settable property of the character from a script and compares what outlives a play session after Stop. It also tries every name on the player's objects: what takes a value must be what the description says a script can set.
- `LuauDefinitionsTests` checks that the definitions file has every class, member and global, and that it has no `require`.
- `ScriptSampleTests` fails when no clean sample script names a class or uses a global. See [Checked by luau-lsp](#checked-by-luau-lsp).
- `TouchCostTests` fails when a tick with parts listened to allocates. Its table of times runs when asked for, like the one below.
- `ScriptCostTests` fails when a crossing allocates. A new kind of crossing gets a row there. Its table of times is a measurement, which runs only when asked for: `dotnet run -c Release --project Test/SpectraEngine.Scripting.Tests -- -explicit only -class "SpectraEngine.Scripting.Tests.ScriptCostTests"`.
- The generator's own tests are in `Test/SpectraEngine.Entities.Tests` (`ScriptApiGenerator*`). They compile its output against stubs of the binding in `ScriptApiHarness`. When the generator calls something new in the binding, the stub gets it too.
- `EntityClassApiTests` holds the built-in entity classes against the script API: no input or output may be named as a member of the node that carries it, and each has to be in the definitions file.
- `EntityClassGapsTests` holds the same names free on every class of nodes, so a new member of `Light` or `BasePart` that a built-in class has as an input fails there.
- `EntityClassBindingTests` asks a node of an entity class for every name of its schema, and compares with the description of the class.
- An object a script is only ever handed in a call, as a mover is handed its `CharacterState`, gets its holder in `ScriptApiDescriptionTests` from that call: the script keeps it and waits a heartbeat. So its properties have to read between calls, and only its writes and its methods are for the call alone.
- `MoverCostTests` fails when a tick of a mover in Luau, or anything a mover does with what it is handed, allocates. Its tables of times run when asked for, like the others. `docs/physics.md` has the mover's seam.

## The files

The demo writes both and opens no window. Neither is committed.

```bash
dotnet run --project SpectraEngine.Executable -- --export-script-types=spectra.d.luau
dotnet run --project SpectraEngine.Executable -- --export-script-api=script-api.json
```

`LuauDefinitions.Write` and `ScriptApiJson.Write` are in Core and need no Luau. `LuauScriptApi.Description` is the data, and reading it loads no native library.

The site's script reference is written from the JSON by `site/scripts/script-api.mjs`: a page for each class, library and entity class, and an overview with the globals. `npm --prefix site run api` runs it after the C# reference, and `npm --prefix site run script-api` runs it alone. Handed a path, it reads that file and starts no engine. The pages are not committed. `ScriptReferenceInputTests` holds the format the script reads to `ScriptApiJson.Format`, and the words it takes an entity class's group from.

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

- A script under `Scripts` or `Guides` is clean: luau-lsp has to say nothing of it. The file Insert Script writes and the demo's `Assets/Scripts/spinner.luau` are checked with them.
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

A clean sample can be one the engine refuses. `Scripts/score_with_extension.luau` asks for a module by a path that ends in `.luau`, which luau-lsp takes and the engine does not, and a guide says so. The day luau-lsp reports it, the clean run fails and the guide's sentence has to change.

The scripts on the site's pages are samples too. A block of Luau on a page is the whole text of one sample, and `SitePageScriptTests` holds every page under `site/src/content/docs` to that. A guide's samples are under `Guides`, in a folder for the page. One that asks for a module by a path is under `Scripts`, where the path is the one a reader's project has.

Each page that shows a script has a test class that plays its samples in a level and holds what the page quotes of Output: `FirstScriptGuideTests`, `MovePartGuideTests`, `TalkToLevelGuideTests`, `HearLevelGuideTests`, `PlayerGuideTests`, `MakeThingsGuideTests` and `MoverGuideTests`. A new page with a script gets one, and a line in `SitePageScriptTests`.

`ComingFromRobloxPageTests` holds the page for a Roblox developer, which shows no script. Its rows on `GetTags`, on an attribute's name and on a destroyed node's tags are held both ways: a row has to be there while the engine answers otherwise than Studio did, and has to go when it answers the same.
