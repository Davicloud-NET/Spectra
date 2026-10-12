using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// A name and a type, as Luau writes them.
internal sealed record LuauParameterModel(string Name, string Type) : IEquatable<LuauParameterModel>;
