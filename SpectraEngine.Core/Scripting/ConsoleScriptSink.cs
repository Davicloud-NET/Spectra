using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// Writes what scripts report as console lines, one for each message. The
/// file, the line and an error's stack travel beside the text, so a reader
/// can take a person there. A print shows wherever the console's output shows.
/// </summary>
public sealed class ConsoleScriptSink : IScriptSink
{
    private readonly IConsoleOutput _output;

    /// <summary>A sink that writes to a console's output.</summary>
    public ConsoleScriptSink(IConsoleOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <inheritdoc/>
    public void Report(in ScriptMessage message) =>
        _output.Write(new ConsoleLine(0, SeverityOf(message.Severity), message.Text)
        {
            Place = message.Script.Length > 0 ? new ConsolePlace(message.Script, message.Line) : null,
            Stack = FramesUnder(in message),
            ProblemNode = message.ProblemNode,
        });

    // The line most often names the first frame's position already. After
    // error(message, 2) it names the caller's, and the frame that raised is
    // still to be shown.
    private static IReadOnlyList<ConsolePlace> FramesUnder(in ScriptMessage message)
    {
        IReadOnlyList<ScriptFrame> stack = message.Stack;
        bool isFirstNamed = stack.Count > 0 && stack[0].Script == message.Script && stack[0].Line == message.Line;
        int first = isFirstNamed ? 1 : 0;
        if (stack.Count <= first)
            return Array.Empty<ConsolePlace>();

        var frames = new ConsolePlace[stack.Count - first];
        for (int i = 0; i < frames.Length; i++)
        {
            ScriptFrame frame = stack[first + i];
            frames[i] = new ConsolePlace(frame.Script, frame.Line, frame.Function);
        }

        return frames;
    }

    private static LogLevel SeverityOf(ScriptSeverity severity) => severity switch
    {
        ScriptSeverity.Error => LogLevel.Error,
        ScriptSeverity.Warning => LogLevel.Warning,
        _ => LogLevel.Information,
    };
}
