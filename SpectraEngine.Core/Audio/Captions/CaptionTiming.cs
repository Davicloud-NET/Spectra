using System;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>How long a caption stays up so that it can be read.</summary>
// Reading-speed guidance, kept in one place to be tuned by eye. Subtitle
// guidelines ask for 12 to 17 characters a second and at least a second on
// screen. Letters and digits are counted and not words, so a language that
// writes no spaces gets its time too.
public static class CaptionTiming
{
    /// <summary>No caption goes sooner than this many seconds after it appeared.</summary>
    public const double MinimumSeconds = 1.0;

    /// <summary>Seconds a reader needs to find a new caption before reading it.</summary>
    public const double NoticeSeconds = 0.5;

    /// <summary>Letters and digits a reader takes in a second.</summary>
    public const double CharactersPerSecond = 15.0;

    /// <summary>The seconds a caption with these words stays up at least.</summary>
    public static double ReadingSeconds(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int count = 0;
        foreach (char letter in text)
        {
            if (char.IsLetterOrDigit(letter))
                count++;
        }

        return Math.Max(MinimumSeconds, NoticeSeconds + (count / CharactersPerSecond));
    }
}
