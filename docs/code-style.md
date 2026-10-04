# Code style

How Spectra code gets written. It started from the DDT guide and was bent to fit an engine.

If a rule makes the code worse, break it and leave a one-line comment saying why.

## Writing

This covers anything a person reads: comments, docs, commit messages, log lines, editor text.

- Plain words and short sentences. Say a thing once.
- No em dashes, no bold lead-ins, no capitals for emphasis.
- Leave out the filler: "deliberately", "silently", "exactly", "load-bearing", "the whole point", "by construction".
- State the fact. Don't argue with a reader who isn't there ("this is not a grid, it is a fault").
- Read it back. If it sounds like a press release or a contract, write it again.

## Comments

Most code needs none. Write one when the code can't say it:

- why it is done this way, when the obvious way is wrong
- a constraint from outside: a driver quirk, a file format, a thread rule
- a trade-off somebody would otherwise "fix"

The rules:

- One or two lines. Three or four for a really nasty why.
- Fragments are fine: `// GL defaults to Less`.
- Don't narrate the code and don't explain a name. If a name needs explaining, rename it.
- No history. No "used to", "no longer", "was changed". Git remembers.
- Don't point at things that move, like line numbers, doc sections or milestone ids.
- `TODO` only with an issue: `// TODO(#123): drop once the GL path is gone`.
- Long reasoning belongs in `docs/`, not above a class.

Public API gets `///`, because that is what IntelliSense shows and what the docs site will be built from:

- `<summary>` says what the thing is for, in a line or two.
- `<param>` and `<returns>` only when they tell you something the signature doesn't.
- No `<remarks>` essays, no `<b>`, no stacks of `<para>`.
- Private and internal members get `//` or nothing.

Before:

```csharp
/// <summary>
/// Restores the before-state for an <em>abandoned gesture</em> rather than
/// for a history step: the roll-back CancelTransaction performs, whose
/// contract is that the scene ends up exactly as the gesture found it.
/// </summary>
/// <remarks>
/// <b>The missing-target rule inverts here, and that is the whole point.</b>
/// ...ten more lines...
/// </remarks>
void RollBack(Scene scene) => Undo(scene);
```

After:

```csharp
/// <summary>Restores the before state when a gesture is cancelled.</summary>
// Unlike Undo, this should also restore a node that left the scene mid-gesture
// when the command can still reach it. A cancelled transaction is discarded,
// so nothing else will put the value back.
void RollBack(Scene scene) => Undo(scene);
```

## Files and types

- One top-level type per file, named after the type. A generic that shares a name with a non-generic goes in `Result{T}.cs`.
- The folder is the namespace. Namespaces are file-scoped, usings above them.
- Nested types are fine when they are private, small and only make sense inside their parent.
- Partial classes are for generators and Avalonia code-behind, not for spreading a big class over several files.

## Size

Aim for the first number, split at the second.

| | Aim | Split |
|---|---|---|
| Class | 300 lines | 400 |
| Method | 40 lines | 60 |
| Parameters | 4 | 6 |
| Nesting | 3 | 4 |
| Test class | 500 lines | 700 |

Split by job, not by line count. A class has two jobs when its one-line description needs an "and", when some fields are only used by some methods, or when it has section banners like `// --- Snap field ---`. Pull the second job out into a type whose name says what it does. `Helper`, `Utils` and `Manager` are drawers, not names.

Flat data can run long: lookup tables, format constants, diagnostic codes. So can a codec that hangs together. Take out what it duplicates before chopping it up.

## Naming

- The usual .NET conventions: `PascalCase` types and members, `camelCase` locals and parameters, `_camelCase` private fields, `I` for interfaces.
- Whole words. Abbreviate only what the field already abbreviates: `Id`, `Uv`, `Bsp`, `Csg`, `Aabb`, `Rtv`, `Pso`.
- Booleans read as a question: `IsVisible`, `HasBrush`, `CanPlay`.
- American spelling in identifiers, the way .NET spells it: `Color`, `Center`, `Normalize`.

## C#

- Nullable is on. Don't use `!` to shut it up.
- `sealed` unless the class is built to be inherited.
- Check arguments at public entry points with `ArgumentNullException.ThrowIfNull` and friends.
- AOT-safe: no reflection, no `dynamic`, no runtime codegen.
- Nothing in the frame loop allocates. No LINQ, closures or string formatting there. Elsewhere, LINQ where it reads better and a loop where that is clearer.
- Never `.Result` or `.Wait()`.
- Catch what you can handle. `catch (Exception)` belongs at a boundary that reports the failure, with a one-line comment saying so.

## Tests

- A test's name is a sentence about behaviour: `A_new_command_invalidates_the_redo_tail`.
- One test class per class or feature. Past 500 lines, split it by behaviour.
- One behaviour per test: arrange, act, assert.
- No sleeping to wait for something. Wait for the condition, with a timeout.
- Fakes get their own files.
- A real fixture beats a made-up one.
- Run a suite with `dotnet run --project Test/<name>`, not `dotnet test`.

## Commits

- As short as possible. Usually the subject line is the whole message.
- Say what changed: `Fix Assimp load on Linux`.
- No co-author trailers.

## Not checked yet

Nothing in the build enforces this today. The plan is an `.editorconfig` and a test that watches class size, comment length and one type per file.
