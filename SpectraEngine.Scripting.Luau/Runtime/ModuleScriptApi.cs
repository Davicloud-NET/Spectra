using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>A script that runs when another script asks for it.</summary>
[ScriptClass("ModuleScript", Extends = "Instance")]
internal static partial class ModuleScriptApi;
