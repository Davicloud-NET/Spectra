using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// A property, a library constant, a global value or a signal. ReadFormat is
// the C# that reads it, with {0} for the object. Sets names a member of
// SettableMemberId, and the generator then implements the partial method
// with Access and SelfName. SetBy names the object's method a write goes to.
internal sealed record ScriptPropertyModel(
    string LuauName,
    string Summary,
    ScriptTypeModel Type,
    string ReadFormat,
    string MemberName,
    string Access,
    string SelfName,
    string Sets,
    string SetBy,
    string CannotSet,
    bool IsSignal,
    EquatableArray<LuauParameterModel> SignalParameters,
    bool IsByHand,
    string Deprecated) : IEquatable<ScriptPropertyModel>;
