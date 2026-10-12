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

`Name` is only needed when a script's name is not the member's own.

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

A property that cannot be set answers "Property is read only". `CannotSet` gives it other words.

### Written by hand

Some members cannot be generated: `CFrame.new` takes six shapes of arguments, `task.wait` yields. Such a member takes a `LuauStack` and returns how many values it pushed, as every host function does. Its attribute says what a script may hand it, as Luau writes it:

```csharp
/// <summary>Calls the function every time the signal fires, with what it was fired with.</summary>
[ScriptMethod(Parameters = "callback: (T...) -> ()", Returns = "RBXScriptConnection")]
private int Connect(LuauStack stack) => Listen(stack, "Connect", isOnce: false);
```

In a static class the object comes after the stack: `static int GetComponents(LuauStack stack, CFrame self)`. Several `[ScriptOverload]` attributes declare several shapes.

Two more things stay by hand:

- An operator. `[ScriptOperator("*", "Vector3", "Vector3")]` goes on the method that implements it, and that method is put into the metatable by hand (`CFrameMembers`).
- What is set outside the script API's own setup: `script`, which each script gets for itself, and `warn`, which a host has before it has a level. `ByHand = true` on the attribute declares it without binding it.

## What is generated

For a class or a value, in the other half of the declaring class:

- `ScriptRead`, `ScriptWrite` and `ScriptInvoke`: a read, a write and a call of a member, each a switch on the member's atom. A write of a settable property calls the door. Every other write answers that the member is read only.
- one method for each typed method, which checks every argument, converts it, calls and pushes the result. A wrong argument is refused in Roblox's words: `invalid argument #2 to 'Lerp' (number expected, got string)`.
- `ScriptMethods`: the functions a script gets when it reads a method and does not call it.
- `ScriptDescription`: the class as data.

For a library and for globals: `ScriptInstall`, which makes the table or sets the globals, and the description.

One more file, `ScriptApiCatalog.g.cs`, numbers every member name (`ScriptAtoms`) and every class (`ScriptClassIds`), and gathers the descriptions. A name's number is its atom: Luau hands it over in place of the name, so `part:FindFirstChild(...)` reaches its code through `__namecall` and a switch on a number, with no string compared.

The metatables are still set up by hand, in `ObjectMembers`, `CFrameMembers` and `Vector3Members`. Each finds the object, calls the generated switch, and puts a name that means nothing into the words a script reads.

A bad declaration is an error at the attribute, `SPS001` to `SPS009`. `SpectraEngine.Entities.Generator/AnalyzerReleases.Unshipped.md` lists them.

## What the tests then demand

- `ScriptApiDescriptionTests` asks an object of every class for every name, and compares with the description in both directions. A new class needs an object in its `Holders`. A member that a metatable answers by hand and no attribute declares fails here.
- `SettableListTests` holds the settable properties of the description equal to the list of `ScriptWrites`, by name, type and member.
- `StopPromiseTests` writes to every settable member and compares the level's saved bytes after Stop.
- `LuauDefinitionsTests` checks that the definitions file has every class, member and global.
- `ScriptCostTests` fails when a crossing allocates. A new kind of crossing gets a row there.
- The generator's own tests are in `Test/SpectraEngine.Entities.Tests` (`ScriptApiGenerator*`). They compile its output against stubs of the binding in `ScriptApiHarness`. When the generator calls something new in the binding, the stub gets it too.

## The files

The demo writes both and opens no window. Neither is committed.

```bash
dotnet run --project SpectraEngine.Executable -- --export-script-types=spectra.d.luau
dotnet run --project SpectraEngine.Executable -- --export-script-api=script-api.json
```

`LuauDefinitions.Write` and `ScriptApiJson.Write` are in Core and need no Luau. `LuauScriptApi.Description` is the data, and reading it loads no native library.

The syntax of a definitions file has no specification, and `luau-lsp` has not checked this one yet. Every construct is written in one place in `LuauDefinitions`, so a correction is one line there and one in `LuauDefinitionsTests`.
