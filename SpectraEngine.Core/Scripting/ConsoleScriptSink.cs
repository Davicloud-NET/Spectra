using SpectraEngine.Core.ConsoleSystem;
using System;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// Writes what scripts report as console lines, with the file and the line
/// in the text. So a print shows wherever the console's output shows.
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
    public void Report(in ScriptMessage message)
    {
        Write(message.Severity, Describe(in message));

        // The first frame is the position the line already names.
        for (int i = 1; i < message.Stack.Count; i++)
            Write(message.Severity, Describe(message.Stack[i]));
    }

    /// <summary>The message as one line: <c>Scripts/door.luau:12: the text</c>.</summary>
    public static string Describe(in ScriptMessage message) =>
        message.Position.Length > 0 ? $"{message.Position}: {message.Text}" : message.Text;

    private static string Describe(ScriptFrame frame) =>
        frame.Function is null
            ? $"    {frame.Script}:{frame.Line}"
            : $"    {frame.Script}:{frame.Line} in {frame.Function}";

    private void Write(ScriptSeverity severity, string line)
    {
        switch (severity)
        {
            case ScriptSeverity.Error:
                _output.Error(line);
                break;
            case ScriptSeverity.Warning:
                _output.Warn(line);
                break;
            default:
                _output.Print(line);
                break;
        }
    }
}
