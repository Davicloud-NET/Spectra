using Microsoft.Extensions.Logging;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>One line of console output, as a value any thread can hold.</summary>
/// <param name="Sequence">
/// The line's number, counted from zero for each console. A dropped line keeps
/// its number, so a gap says how many lines are missing.
/// </param>
/// <param name="Severity">Information for a plain line, Warning or Error for the others.</param>
/// <param name="Text">The line.</param>
public readonly record struct ConsoleLine(long Sequence, LogLevel Severity, string Text);
