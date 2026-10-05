namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// What a caption puts into words. The kind comes from where the text lives,
/// never from anything in the text.
/// </summary>
public enum CaptionKind
{
    /// <summary>Speech. The sound has a subtitle file beside it.</summary>
    Voice,

    /// <summary>Any other sound. It has a line in the language's caption file.</summary>
    Sound,
}
