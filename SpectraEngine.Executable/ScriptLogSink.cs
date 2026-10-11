using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Scripting;
using System.Text;

namespace SpectraEngine.Executable;

// Writes what a script reports to the log, where the console shows it too.
internal sealed class ScriptLogSink(ILogger logger) : IScriptSink
{
    // What every script line in the log starts with.
    public const string Prefix = "[script]";

    public void Report(in ScriptMessage message)
    {
        string line = Describe(in message);
        switch (message.Severity)
        {
            case ScriptSeverity.Error:
                logger.LogError("{Line}", line);
                break;
            case ScriptSeverity.Warning:
                logger.LogWarning("{Line}", line);
                break;
            default:
                logger.LogInformation("{Line}", line);
                break;
        }
    }

    // "[script] Scripts/door.luau:12: the text", and under an error one line
    // for each function it came through.
    public static string Describe(in ScriptMessage message)
    {
        StringBuilder line = new(Prefix);
        if (message.Position.Length > 0)
            line.Append(' ').Append(message.Position).Append(':');
        line.Append(' ').Append(message.Text);

        foreach (ScriptFrame frame in message.Stack)
        {
            line.AppendLine().Append("    ").Append(frame.Script).Append(':').Append(frame.Line);
            if (frame.Function is not null)
                line.Append(" in ").Append(frame.Function);
        }

        return line.ToString();
    }
}
