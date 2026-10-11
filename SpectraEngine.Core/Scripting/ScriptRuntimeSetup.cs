using SpectraEngine.Core.Entities;

namespace SpectraEngine.Core.Scripting;

/// <summary>What a script runtime is made from.</summary>
/// <param name="World">The level it runs in. Its queue is where a waiting script waits.</param>
/// <param name="Sink">Where its scripts report what they print and how they fail.</param>
/// <param name="Source">Where the text of its scripts comes from.</param>
public sealed record ScriptRuntimeSetup(EntityWorld World, IScriptSink Sink, IScriptSource Source);
