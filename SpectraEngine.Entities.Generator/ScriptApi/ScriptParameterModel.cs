using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// A parameter the generated code reads. Default is a C# expression, or empty.
internal sealed record ScriptParameterModel(
    string Name,
    ScriptTypeModel Type,
    bool IsOptional,
    string Default) : IEquatable<ScriptParameterModel>;
