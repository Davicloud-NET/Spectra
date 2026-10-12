using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// A property, a library constant, a global value or a signal. ReadFormat is
// the C# that reads it, with {0} for the object. Sets is a member of
// SettableMemberId, or empty. One with Sets is a partial method the generator
// implements, with the accessibility Access and the parameter SelfName.
internal sealed record ScriptPropertyModel(
    string LuauName,
    string Summary,
    ScriptTypeModel Type,
    string ReadFormat,
    string MemberName,
    string Access,
    string SelfName,
    string Sets,
    string CannotSet,
    bool IsSignal,
    EquatableArray<LuauParameterModel> SignalParameters,
    bool IsByHand,
    string Deprecated) : IEquatable<ScriptPropertyModel>;
