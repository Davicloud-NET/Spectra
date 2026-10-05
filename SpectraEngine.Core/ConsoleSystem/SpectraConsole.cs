using SpectraEngine.Core.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>
/// One engine's command line: the commands it knows and the lines they wrote.
/// Render thread only.
/// </summary>
public sealed class SpectraConsole
{
    /// <summary>How many commands one <see cref="Drain"/> runs. The rest keep for the next.</summary>
    public const int MaxCommandsPerDrain = 512;

    /// <summary>The most drains a <c>wait</c> holds a line for.</summary>
    public const int MaxWaitFrames = 300;

    // A line that starts with this is script, not a command.
    private const char ScriptSigil = '>';

    // Lines up to this long are tokenized on the stack.
    private const int StackTokenLength = 256;

    // Text a wait or the command limit held back, oldest first. It runs
    // before anything still queued on the host.
    private readonly List<string> _held = [];

    // Drains left before anything runs again. Set by wait.
    private int _waitFrames;

    /// <summary>Creates a console that knows <c>help</c>, <c>echo</c> and <c>wait</c>.</summary>
    public SpectraConsole() => BuiltinConsoleCommands.Register(this);

    /// <summary>The commands this console can run.</summary>
    public ConCommandTable Commands { get; } = new();

    /// <summary>The lines written since they were last taken.</summary>
    public ConsoleOutput Output { get; } = new();

    /// <summary>
    /// Runs every command on <paramref name="line"/>, in order. A command that
    /// fails is reported in <see cref="Output"/> and the rest still run. A
    /// <c>wait</c> keeps what follows it for a later <see cref="Drain"/>.
    /// </summary>
    public void Execute(string line, in ConsoleFrame frame)
    {
        ArgumentNullException.ThrowIfNull(line);

        int budget = int.MaxValue;
        if (!RunLine(line, in frame, ref budget, out int stoppedAt))
            Hold(line, stoppedAt, first: false);
    }

    /// <summary>
    /// Runs the lines waiting on <paramref name="host"/>, oldest first, up to
    /// <see cref="MaxCommandsPerDrain"/> commands. Called once a frame.
    /// </summary>
    public void Drain(EngineHost host, in ConsoleFrame frame)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (_waitFrames > 0 && --_waitFrames > 0)
            return;

        int budget = MaxCommandsPerDrain;
        bool ran = false;
        while (TryNextLine(host, out string? line))
        {
            ran = true;
            if (RunLine(line, in frame, ref budget, out int stoppedAt))
                continue;

            Hold(line, stoppedAt, first: true);
            break;
        }

        // The replies go out with this frame instead of waiting for the interval.
        if (ran)
            host.MarkDirty();
    }

    // Stops the drain that is running and holds everything for this many more.
    internal void HoldFor(int frames) => _waitFrames = frames;

    private bool TryNextLine(EngineHost host, [NotNullWhen(true)] out string? line)
    {
        if (_held.Count == 0)
            return host.TryTakeConsoleLine(out line);

        line = _held[0];
        _held.RemoveAt(0);
        return true;
    }

    private void Hold(string line, int from, bool first)
    {
        if (line.AsSpan(from).IsWhiteSpace())
            return;

        string rest = from == 0 ? line : line[from..];
        if (first)
            _held.Insert(0, rest);
        else
            _held.Add(rest);
    }

    // False when a wait or the budget stopped the line early. stoppedAt is
    // then where the commands that did not run begin.
    private bool RunLine(string line, in ConsoleFrame frame, ref int budget, out int stoppedAt)
    {
        stoppedAt = 0;

        if (line.AsSpan().TrimStart().StartsWith(ScriptSigil))
        {
            Output.Error($"Luau is not built yet, so a line that starts with {ScriptSigil} cannot run.");
            return true;
        }

        bool onStack = line.Length <= StackTokenLength;
        Span<char> text = onStack ? stackalloc char[StackTokenLength] : new char[line.Length];
        Span<Range> tokens = onStack ? stackalloc Range[StackTokenLength] : new Range[line.Length];

        int position = 0;
        while (true)
        {
            stoppedAt = position;
            if (!ConsoleTokenizer.TryNextCommand(line, ref position, out ReadOnlySpan<char> command))
                return true;

            if (budget == 0)
                return false;

            budget--;

            int count = ConsoleTokenizer.Tokenize(
                command, text, tokens, out ReadOnlySpan<char> rawArgs, out bool quoteLeftOpen);

            if (quoteLeftOpen)
                Output.Warn($"a quote was not closed, so it runs to the end of the line: {command}");

            Run(text, tokens[..count], rawArgs, in frame);

            if (_waitFrames > 0)
            {
                stoppedAt = position;
                return false;
            }
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
