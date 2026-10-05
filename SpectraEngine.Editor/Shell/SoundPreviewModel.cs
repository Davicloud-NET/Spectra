using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Which sound the engine is playing by itself, for the play buttons beside
/// sound files. UI thread only.
/// </summary>
// What plays is the engine's to say, from each snapshot. A press only sends
// what the user wants, so a file that fails to load never shows as playing.
public sealed class SoundPreviewModel : ObservableObject
{
    private string _playing = string.Empty;

    /// <summary>
    /// Raised with what to ask the engine for: a content path to play, or an
    /// empty one to stop.
    /// </summary>
    public event Action<string>? Requested;

    /// <summary>The content path the engine says is playing, or empty when none is.</summary>
    public string Playing
    {
        get => _playing;
        private set => Set(ref _playing, value);
    }

    /// <summary>Whether the engine is playing this file.</summary>
    // Content paths compare without case, as the asset manager's keys do.
    public bool IsPlaying(string? contentPath) =>
        !string.IsNullOrEmpty(contentPath)
        && string.Equals(_playing, contentPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Takes what a snapshot says is playing.</summary>
    public void Apply(string? previewing) => Playing = previewing ?? string.Empty;

    /// <summary>The session is over, and its sound with it.</summary>
    public void EndSession() => Playing = string.Empty;

    /// <summary>
    /// A file's play button was pressed: asks for the file, or for a stop when
    /// it is the one playing.
    /// </summary>
    public void Press(string? contentPath)
    {
        if (string.IsNullOrEmpty(contentPath))
            return;

        Requested?.Invoke(IsPlaying(contentPath) ? string.Empty : contentPath);
    }
}
