using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// A method or a function. CallFormat is the C# expression that names it,
// with {0} for the object. A function by the stack is written by hand and
// takes a LuauStack. TakesSelf says the object is its own parameter. One
// by hand is only described.
internal sealed record ScriptFunctionModel(
    string LuauName,
    string Summary,
    string CallFormat,
    bool IsStatic,
    bool IsByStack,
    bool TakesSelf,
    bool IsByHand,
    EquatableArray<ScriptParameterModel> Parameters,
    EquatableArray<ScriptTypeModel> Returns,
    EquatableArray<ScriptSignatureModel> Signatures,
    string Deprecated) : IEquatable<ScriptFunctionModel>;
