namespace SpectraEngine.Core.Audio.Captions;

/// <summary>One line of a sound's captions, and when in the sound it is said.</summary>
/// <param name="Start">Seconds into the sound at which the line starts.</param>
/// <param name="End">
/// Seconds into the sound at which the line ends. Infinity for a line that
/// lasts as long as the sound does.
/// </param>
/// <param name="Speaker">Who says it, or null when the file names nobody.</param>
/// <param name="Text">The words. A line break in them is one the writer chose.</param>
public readonly record struct CaptionLine(double Start, double End, string? Speaker, string Text);
