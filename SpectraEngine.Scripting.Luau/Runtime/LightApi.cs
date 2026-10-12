using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>A node that lights the level. Each kind of light is a class of its own.</summary>
[ScriptClass("Light", Extends = "Instance")]
internal static partial class LightApi;
