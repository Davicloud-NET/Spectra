using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Everything the emitter needs about one attributed class. Values only, as
// in EntityModel: a symbol here would pin the compilation. ContainingTypes
// are outermost first, as the partial declarations the emitter reopens.
// Self is the type of the object the members are of, or null when none is.
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
