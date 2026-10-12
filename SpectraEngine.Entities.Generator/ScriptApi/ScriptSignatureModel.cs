using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// One shape a function is called in, for the description.
internal sealed record ScriptSignatureModel(
    EquatableArray<LuauParameterModel> Parameters,
    string Returns) : IEquatable<ScriptSignatureModel>;
