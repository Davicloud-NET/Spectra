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

A class may declare no member at all, as `Part` and `SpotLight` do today:

```csharp
/// <summary>A light that shines in a cone.</summary>
[ScriptClass("SpotLight", Extends = "Light")]
internal static partial class SpotLightApi;
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
| `SceneNode` | `Instance` |
| a class with `[ScriptClass]` | that class |

A new kind of value is added in three places: `ScriptTypeMap` and `ScriptMarshalling` in the generator, and a reader in `ScriptArguments`.

### A property a script can set

A script changes a node through `ScriptWrites` and nothing else, so Stop can put it back. `ScriptWrites` keeps its own list, since Core cannot see the binding. A settable property names the door's member and has no body:

```csharp
/// <summary>Where the node is in the world.</summary>
[ScriptProperty(Sets = SettableMemberId.Position)]
public static partial Vector3 Position(SceneNode node);
```

The generator writes the body, which reads the same member of the door. So what a script reads is what it wrote. The member itself is added to `ScriptWrites` first: a `SettableMemberId`, a row in its list, a case in `Read` and in `TryWrite`, and a `JournalKind`.

A property that cannot be set answers "Property is read only". `CannotSet` gives it other words. The definitions file marks it `read`, so the type checker refuses the write the binding would refuse.

### A property of an object that is no node

The door sets nodes. An object of a class that is not static takes a write itself: `SetBy` names a method of the class that gets the value and returns null, or why it refused the value. The script reads that as its error.

```csharp
/// <summary>How fast the player walks, in units a second. A unit is a metre, so Roblox's 16 is about 4.5 here. Stop puts back what a script set.</summary>
[ScriptProperty(SetBy = nameof(SetWalkSpeed))]
public float WalkSpeed => _door.WalkSpeed ?? throw Left();

private string? SetWalkSpeed(float value) => _door.TrySetWalkSpeed(value, out string? refused) ? null : refused;
```

The value is a number, a boolean, a string, a `Vector3` or a `CFrame`. One of another kind is refused before the method is called, in the words the door uses.

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

Two more things stay by hand:

- An operator. `[ScriptOperator("*", "Vector3", "Vector3")]` goes on the method that implements it, and that method is put into the metatable by hand (`CFrameMembers`).
- What is set outside the script API's own setup: `script`, which each script gets for itself, and `warn`, which a host has before it has a level. `ByHand = true` on `[ScriptProperty]` or `[ScriptFunction]` declares it without binding it.

### A global the type checker has too

`require` is declared like any global function with by-hand arguments (`ModuleLoader`), so the reference has it with its summary. The definitions file leaves it out. The type checker has a `require` of its own, which follows a path to its file and gives the module its type. A declaration in the file would take its place, and every module would be `any`. `LuauDefinitions.IsTheCheckersOwn` holds the names that are left out this way.

## What is generated

For a class or a value, in the other half of the declaring class:

- `ScriptRead`, `ScriptWrite` and `ScriptInvoke`: a read, a write and a call of a member, each a switch on the member's atom. A write of a settable property calls the door. Every other write answers that the member is read only. A name the class does not declare goes to the class it extends, through `ScriptBases` in the catalogue.
- one method for each typed method, which checks every argument, converts it, calls and pushes the result. A wrong argument is refused in Roblox's words: `invalid argument #2 to 'Lerp' (number expected, got string)`.
- `ScriptMethods`: the functions a script gets when it reads a method and does not call it.
- `ScriptDescription`: the class as data.

For a library and for globals: `ScriptInstall`, which makes the table or sets the globals, and the description.

One more file, `ScriptApiCatalog.g.cs`, numbers every member name (`ScriptAtoms`) and every class (`ScriptClassIds`), and gathers the descriptions. A name's number is its atom: Luau hands it over in place of the name, so `part:FindFirstChild(...)` reaches its code through `__namecall` and a switch on a number, with no string compared.

The catalogue also holds the switches of every class that declares no member, as above.

The metatables are still set up by hand, in `ObjectMembers`, `CFrameMembers` and `Vector3Members`. Each finds the object, calls the generated switch, and puts a name that means nothing into the words a script reads. For a node, `ObjectMembers` goes through `NodeMembers`, which picks the class.

A bad declaration is an error, `SPS001` to `SPS010`. `SpectraEngine.Entities.Generator/AnalyzerReleases.Unshipped.md` lists them. One about what an attribute says points at the attribute. One about the member itself points at the member: no summary, a type that cannot cross, a missing object parameter, a shape that cannot be called. A parameter Luau cannot name is marked at the parameter, and a class that extends what it cannot is marked at the class.

## What the tests then demand

- `ScriptApiDescriptionTests` asks an object of every class for every name, and compares with the description in both directions. A new class needs an object in its `Holders`. The object has to be of that class and of none below it: a `Part` as the holder of `Instance` would answer the members of `Part` too. A member that a metatable answers by hand and no attribute declares fails here.
- `NodeClassScriptTests` reads `NodeMembers` and fails when a class of nodes is sent to another script class than the one of its name.
- `SettableListTests` holds the settable properties of the description equal to the list of `ScriptWrites`, by name, type and member.
- `StopPromiseTests` writes to every settable member and compares the level's saved bytes after Stop.
- `PlayerSettingTests` sets every settable property of the character from a script and compares what outlives a play session after Stop. It also tries every name on the player's objects: what takes a value must be what the description says a script can set.
- `LuauDefinitionsTests` checks that the definitions file has every class, member and global, and that it has no `require`.
- `ScriptCostTests` fails when a crossing allocates. A new kind of crossing gets a row there. Its table of times is a measurement, which runs only when asked for: `dotnet run -c Release --project Test/SpectraEngine.Scripting.Tests -- -explicit only -class "SpectraEngine.Scripting.Tests.ScriptCostTests"`.
- The generator's own tests are in `Test/SpectraEngine.Entities.Tests` (`ScriptApiGenerator*`). They compile its output against stubs of the binding in `ScriptApiHarness`. When the generator calls something new in the binding, the stub gets it too.

## The files

The demo writes both and opens no window. Neither is committed.

```bash
dotnet run --project SpectraEngine.Executable -- --export-script-types=spectra.d.luau
dotnet run --project SpectraEngine.Executable -- --export-script-api=script-api.json
```

`LuauDefinitions.Write` and `ScriptApiJson.Write` are in Core and need no Luau. `LuauScriptApi.Description` is the data, and reading it loads no native library.

The syntax of a definitions file has no specification, and `luau-lsp` has not checked this one yet. Every construct is written in one place in `LuauDefinitions`, so a correction is one line there and one in `LuauDefinitionsTests`.
