using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// An operator a class or a value stands on the left of. Right is empty for
// one that takes nothing on its right.
internal sealed record ScriptOperatorModel(
    string Operator,
    string Right,
    string Returns) : IEquatable<ScriptOperatorModel>;
