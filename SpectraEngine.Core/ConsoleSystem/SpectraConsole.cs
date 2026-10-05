using System;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>
/// One engine's command line: the commands it knows and the lines they wrote.
/// Render thread only.
/// </summary>
public sealed class SpectraConsole
{
    // A line that starts with this is script, not a command.
    private const char ScriptSigil = '>';

    // Lines up to this long are tokenized on the stack.
    private const int StackTokenLength = 256;

    /// <summary>Creates a console that knows <c>help</c> and <c>echo</c>.</summary>
    public SpectraConsole() => BuiltinConsoleCommands.Register(Commands);

    /// <summary>The commands this console can run.</summary>
    public ConCommandTable Commands { get; } = new();

    /// <summary>The lines written since they were last taken.</summary>
    public ConsoleOutput Output { get; } = new();

    /// <summary>
    /// Runs every command on <paramref name="line"/>, in order. A command that
    /// fails is reported in <see cref="Output"/> and the rest still run.
    /// </summary>
    public void Execute(string line, in ConsoleFrame frame)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.AsSpan().TrimStart().StartsWith(ScriptSigil))
        {
            Output.Error($"Luau is not built yet, so a line that starts with {ScriptSigil} cannot run.");
            return;
        }

        bool onStack = line.Length <= StackTokenLength;
        Span<char> text = onStack ? stackalloc char[StackTokenLength] : new char[line.Length];
        Span<Range> tokens = onStack ? stackalloc Range[StackTokenLength] : new Range[line.Length];

        int position = 0;
        while (ConsoleTokenizer.TryNextCommand(line, ref position, out ReadOnlySpan<char> command))
        {
            int count = ConsoleTokenizer.Tokenize(
                command, text, tokens, out ReadOnlySpan<char> rawArgs, out bool quoteLeftOpen);

            if (quoteLeftOpen)
                Output.Warn($"a quote was not closed, so it runs to the end of the line: {command}");

            Run(text, tokens[..count], rawArgs, in frame);
        }
    }

    private void Run(
        ReadOnlySpan<char> text,
        ReadOnlySpan<Range> tokens,
        ReadOnlySpan<char> rawArgs,
        in ConsoleFrame frame)
    {
        ReadOnlySpan<char> name = text[tokens[0]];
        if (!Commands.TryGet(name, out ConCommand? command))
        {
            Output.Error($"no command named '{name}'. Type help.");
            return;
        }

        var args = new ConArgs(text, tokens, rawArgs, Output, in frame);
        try
        {
            command.Handler(in args);
        }
        // The boundary for a command's own failure: report it, and the rest of the line still runs.
        catch (Exception exception)
        {
            Output.Error($"{command.Name} failed. {exception.GetType().Name}: {exception.Message}");
        }
    }
}
