using System;

namespace SpectraEngine.Core.Inspection;

/// <summary>One wire that arrives at the selected entity.</summary>
/// <param name="SourceId">The node of the entity that sends it.</param>
/// <param name="SourceName">That node's name.</param>
/// <param name="Output">The sender's output.</param>
/// <param name="Input">The input it asks for here.</param>
public readonly record struct EntityIncomingInfo(Guid SourceId, string SourceName, string Output, string Input);
