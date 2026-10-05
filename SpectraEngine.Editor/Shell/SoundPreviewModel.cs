using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Audio;
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
    private string _playingSound = string.Empty;

    /// <summary>
    /// Takes a request to the engine: a content path to play, or an empty one
    /// to stop. Returns false when no engine is running to take it.
    /// </summary>
    public Func<string, bool>? Send { get; set; }

    /// <summary>Raised when a press found no engine to ask, so the shell can say so.</summary>
    public event Action? NotSent;

    /// <summary>The content path the engine says is playing, or empty when none is.</summary>
    public string Playing
    {
        get => _playing;
        private set
        {
            if (string.Equals(_playing, value, StringComparison.Ordinal))
                return;

            // Both before anyone is told: a button asks IsPlaying as it hears.
            bool had = HasPlaying;
            _playing = value;
            _playingSound = SoundOf(value);

            Raise();
            Raise(nameof(PlayingName));
            if (had != HasPlaying)
                Raise(nameof(HasPlaying));
        }
    }

    /// <summary>Whether the engine is playing a file.</summary>
    public bool HasPlaying => _playing.Length > 0;

    /// <summary>The playing file's name without its folder, or empty when none plays.</summary>
    public string PlayingName => _playing[(_playing.LastIndexOf('/') + 1)..];

    /// <summary>Whether the engine is playing this file.</summary>
    public bool IsPlaying(string? contentPath) =>
        !string.IsNullOrEmpty(contentPath)
        && _playingSound.Length > 0
        && string.Equals(_playingSound, SoundOf(contentPath), StringComparison.OrdinalIgnoreCase);

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

        Ask(IsPlaying(contentPath) ? string.Empty : contentPath);
    }

    /// <summary>Asks for a stop, whichever file plays. Nothing is asked when none does.</summary>
    public void Stop()
    {
        if (HasPlaying)
            Ask(string.Empty);
    }

    private void Ask(string request)
    {
        if (Send?.Invoke(request) != true)
            NotSent?.Invoke();
    }

    // What a path plays, as the engine finds it: the cooked file beside it.
    // So one sound is one sound with either slash, a slash in front, any case
    // and any of the extensions a sound can be written with.
    private static string SoundOf(string contentPath)
    {
        if (contentPath.Length == 0)
            return string.Empty;

        try
        {
            return AudioContentPath.CookedPathFor(ContentRoot.NormalizeRelativePath(contentPath));
        }
        catch (ArgumentException)
        {
            // Not a content path. It names no sound, so it only matches itself.
            return contentPath;
        }
    }
}
