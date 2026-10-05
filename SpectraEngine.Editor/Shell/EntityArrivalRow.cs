using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>One wire that arrives at the selected entity, as the Receives list shows it.</summary>
/// <param name="SourceId">The node of the entity that sends it.</param>
/// <param name="SourceName">That entity's name.</param>
/// <param name="Output">The sender's output.</param>
/// <param name="Input">The input it asks for here.</param>
public sealed record EntityArrivalRow(Guid SourceId, string SourceName, string Output, string Input);
