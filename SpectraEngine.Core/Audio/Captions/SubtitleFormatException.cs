using System.IO;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// A subtitle file was refused at read. The message names the file and the
/// line.
/// </summary>
// IOException so generic content-load handlers catch it.
public sealed class SubtitleFormatException : IOException
{
    /// <param name="origin">The file, as the message should name it.</param>
    /// <param name="line">The line that was refused, counted from one.</param>
    /// <param name="reason">What is wrong with it, with no full stop.</param>
    public SubtitleFormatException(string origin, int line, string reason)
        : base($"{origin}({line}): {reason}")
    {
        Line = line;
        Reason = reason;
    }

    /// <summary>The line that was refused, counted from one.</summary>
    public int Line { get; }

    /// <summary>What is wrong with the line, without the file's name.</summary>
    public string Reason { get; }
}
