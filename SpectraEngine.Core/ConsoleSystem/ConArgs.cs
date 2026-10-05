using SpectraEngine.Core.Entities;
using System;
using System.Globalization;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>
/// One command as it was typed, and what it may reach. Only valid inside the
/// handler it is passed to.
/// </summary>
public readonly ref struct ConArgs
{
    private readonly ReadOnlySpan<char> _text;

    // One range into _text per token. The first is the name.
    private readonly ReadOnlySpan<Range> _tokens;

    internal ConArgs(
        ReadOnlySpan<char> text,
        ReadOnlySpan<Range> tokens,
        ReadOnlySpan<char> rawArgs,
        IConsoleOutput output,
        in ConsoleFrame frame)
    {
        _text = text;
        _tokens = tokens;
        RawArgs = rawArgs;
        Out = output;
        Scene = frame.Scene;
        Entities = frame.Entities;
        IsPlaying = frame.IsPlaying;
    }

    /// <summary>The command's name, in the case it was typed in.</summary>
    public ReadOnlySpan<char> Name => _text[_tokens[0]];

    /// <summary>Everything after the name as it was typed, quotes and escapes included.</summary>
    public ReadOnlySpan<char> RawArgs { get; }

    /// <summary>How many arguments follow the name.</summary>
    public int Count => _tokens.Length - 1;

    /// <summary>
    /// The argument at <paramref name="index"/>, counted from zero after the
    /// name, without its quotes. Empty when no such argument was given.
    /// </summary>
    public ReadOnlySpan<char> this[int index] =>
        (uint)index < (uint)Count ? _text[_tokens[index + 1]] : default;

    /// <summary>Where the command writes its reply.</summary>
    public IConsoleOutput Out { get; }

    /// <summary>The active scene, or null when there is none.</summary>
    public Scene.Scene? Scene { get; }

    /// <summary>The running entity world, or null when the level is not running.</summary>
    public EntityWorld? Entities { get; }

    /// <summary>
    /// Whether play mode is on. With no <see cref="Entities"/>, the level was
    /// replaced during play.
    /// </summary>
    public bool IsPlaying { get; }

    /// <summary>
    /// Reads an argument as a finite number with a dot for the decimal point,
    /// whatever the machine's culture.
    /// </summary>
    public bool TryGetFloat(int index, out float value)
    {
        // NumberStyles.Float has no AllowThousands, so "1,5" fails instead of reading as 15.
        if (float.TryParse(this[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && float.IsFinite(value))
        {
            return true;
        }

        value = 0f;
        return false;
    }
}
