using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// One C# type as a script sees it. ClrType is global::-qualified and has no
// question mark. LuauType has one when the value may be nil.
internal sealed record ScriptTypeModel(
    ScriptValueKind Kind,
    string ClrType,
    bool IsNullable,
    string LuauType) : IEquatable<ScriptTypeModel>;

// A name and a type, as Luau writes them.
internal sealed record LuauParameterModel(string Name, string Type) : IEquatable<LuauParameterModel>;

// One shape a function is called in, for the description.
internal sealed record ScriptSignatureModel(
    EquatableArray<LuauParameterModel> Parameters,
    string Returns) : IEquatable<ScriptSignatureModel>;

// A parameter the generated code reads. Default is a C# literal, or empty.
internal sealed record ScriptParameterModel(
    string Name,
    ScriptTypeModel Type,
    bool IsOptional,
    string Default) : IEquatable<ScriptParameterModel>;

// A property, a library constant, a global value or a signal. ReadFormat is
// the C# expression that reads it, with {0} for the object. Sets is a
// member of SettableMemberId, or empty. A property with Sets is a partial
// method the generator implements: Access is its accessibility and SelfName
// the name of its parameter.
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

internal sealed record ScriptOperatorModel(
    string Operator,
    string Right,
    string Returns) : IEquatable<ScriptOperatorModel>;

// Everything the emitter needs about one attributed class. Values only, as
// in EntityModel: a symbol here would pin the compilation.
//
// ContainingTypes: outermost first, spelled as the partial declarations the
// emitter reopens. Self is the type of the object the members are of, or
// null for a library, for globals and for a class with no members.
internal sealed record ScriptContainerModel(
    ScriptContainerKind Kind,
    string Namespace,
    EquatableArray<string> ContainingTypes,
    string TypeName,
    string FullTypeName,
    string LuauName,
    string Extends,
    string TypeParameters,
    string ChildType,
    string Summary,
    bool IsStatic,
    bool IsPartial,
    ScriptTypeModel? Self,
    EquatableArray<ScriptPropertyModel> Properties,
    EquatableArray<ScriptFunctionModel> Functions,
    EquatableArray<ScriptOperatorModel> Operators,
    EquatableArray<DiagnosticInfo> Diagnostics,
    LocationInfo? Location) : IEquatable<ScriptContainerModel>;
