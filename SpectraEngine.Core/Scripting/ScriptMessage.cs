using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting;

/// <summary>What a script reported: a printed line, a warning or an error.</summary>
/// <param name="Severity">Which of the three.</param>
/// <param name="Text">The line, without the position in front.</param>
/// <param name="Script">The script file it came from, as a content path. Empty when that is not known.</param>
/// <param name="Line">The line in that file, or 0 when that is not known.</param>
/// <param name="Stack">The call stack of an error, innermost first. Empty for everything else.</param>
public readonly record struct ScriptMessage(
    ScriptSeverity Severity,
    string Text,
    string Script,
    int Line,
    IReadOnlyList<ScriptFrame> Stack)
{
    /// <summary>A message with no stack.</summary>
    public ScriptMessage(ScriptSeverity severity, string text, string script, int line)
        : this(severity, text, script, line, Array.Empty<ScriptFrame>())
    {
    }

    /// <summary>The file and the line as <c>Scripts/door.luau:12</c>, or what of it is known.</summary>
    public string Position => Line > 0 ? $"{Script}:{Line}" : Script;
}
