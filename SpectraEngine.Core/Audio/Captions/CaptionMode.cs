namespace SpectraEngine.Core.Audio.Captions;

/// <summary>Which captions show.</summary>
public enum CaptionMode
{
    /// <summary>None.</summary>
    Off,

    /// <summary>Speech only.</summary>
    Voice,

    /// <summary>Speech and every other sound that has a caption.</summary>
    All,
}
