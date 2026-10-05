using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// The captions of one sound in one language: what <see cref="CaptionLibrary"/>
/// answers with.
/// </summary>
public sealed class SoundCaptions
{
    /// <param name="kind">Speech or another sound.</param>
    /// <param name="language">The language the lines are written in.</param>
    /// <param name="lines">The lines, in the order the file has them.</param>
    public SoundCaptions(CaptionKind kind, string language, IReadOnlyList<CaptionLine> lines)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(lines);

        Kind = kind;
        Language = language;
        Lines = lines;
    }

    /// <summary>Speech or another sound.</summary>
    public CaptionKind Kind { get; }

    /// <summary>
    /// The language the lines are written in. Not the one that was asked for
    /// when the library fell back to the project's language.
    /// </summary>
    public string Language { get; }

    /// <summary>
    /// The lines, in the order the file has them. A sound caption is one line
    /// that lasts as long as the sound.
    /// </summary>
    public IReadOnlyList<CaptionLine> Lines { get; }
}
