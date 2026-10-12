using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// One C# type as a script sees it. ClrType is global::-qualified and has no
// question mark. LuauType has one when the value may be nil.
internal sealed record ScriptTypeModel(
    ScriptValueKind Kind,
    string ClrType,
    bool IsNullable,
    string LuauType) : IEquatable<ScriptTypeModel>;
