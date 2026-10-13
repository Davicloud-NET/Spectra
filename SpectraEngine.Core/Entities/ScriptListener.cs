using System;

namespace SpectraEngine.Core.Entities;

/// <summary>One listener of a level, as <see cref="ScriptListeners.CopyTo"/> lists it.</summary>
/// <param name="Node">The node whose fire it hears.</param>
/// <param name="Name">The name it is fired under, such as an output's.</param>
/// <param name="By">The script that listens and where it connected. No node for a listener a host added.</param>
public readonly record struct ScriptListener(Guid Node, string Name, ScriptPlace By);
