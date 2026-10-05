namespace SpectraEngine.Core.Audio.Captions;

/// <summary>What is wrong with a line of a caption file.</summary>
public enum CaptionFileProblemKind
{
    /// <summary>The line is not a sound's path, an equals sign and words. It is skipped.</summary>
    NotACaption,

    /// <summary>The sound has a caption earlier in the file. The later one is used.</summary>
    Repeated,
}
