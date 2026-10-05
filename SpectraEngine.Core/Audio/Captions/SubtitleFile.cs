using System.Collections.Generic;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>One sound's subtitles in one language, as <see cref="SubtitleReader"/> read them.</summary>
public sealed class SubtitleFile
{
    internal SubtitleFile(List<CaptionLine> lines, List<int> sourceLines, List<SubtitleUnreadPart> unread)
    {
        Lines = lines;
        SourceLines = sourceLines;
        Unread = unread;
    }

    /// <summary>The cues that have words, in file order.</summary>
    public IReadOnlyList<CaptionLine> Lines { get; }

    /// <summary>
    /// The line of the file each cue's times are on, counted from one. One
    /// for each of <see cref="Lines"/>, in the same order.
    /// </summary>
    public IReadOnlyList<int> SourceLines { get; }

    /// <summary>What the file uses that the engine does not read, each named once.</summary>
    public IReadOnlyList<SubtitleUnreadPart> Unread { get; }
}
