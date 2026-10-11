using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>One line of console output, as a value any thread can hold.</summary>
/// <param name="Sequence">
/// The line's number, counted from zero for each console. A dropped line keeps
/// its number, so a gap says how many lines are missing.
/// </param>
/// <param name="Severity">Information for a plain line, Warning or Error for the others.</param>
/// <param name="Text">The line, without its place.</param>
public readonly record struct ConsoleLine(long Sequence, LogLevel Severity, string Text)
{
    private readonly IReadOnlyList<ConsolePlace>? _stack;

    /// <summary>Where the line came from, or null for a line with no place.</summary>
    public ConsolePlace? Place { get; init; }

    /// <summary>
    /// The calls an error came through, innermost first, without the one
    /// <see cref="Place"/> names. Empty for every other line.
    /// </summary>
    public IReadOnlyList<ConsolePlace> Stack
    {
        get => _stack ?? Array.Empty<ConsolePlace>();
        init => _stack = value;
    }

    /// <summary>
    /// The node this line is a standing problem of, such as a script that did
    /// not start. Empty for a line that only says what happened.
    /// </summary>
    public Guid ProblemNode { get; init; }

    /// <summary>
    /// The line for a log or a terminal, where nobody can click: the place in
    /// front of the text, as <c>Scripts/door.luau:12: the text</c>.
    /// </summary>
    public string PlainText => Place is { } place ? $"{place.Position}: {Text}" : Text;
}
