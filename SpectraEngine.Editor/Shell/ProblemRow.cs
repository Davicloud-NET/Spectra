using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// One row of a condition that is found by looking at the scene, for
/// <see cref="ProblemList.Replace"/>.
/// </summary>
/// <param name="Subject">What tells this row from the others of its template.</param>
/// <param name="Message">What the row says.</param>
/// <param name="NodeId">The node that activating the row selects, or <see cref="Guid.Empty"/>.</param>
public readonly record struct ProblemRow(string Subject, string Message, Guid NodeId = default);
