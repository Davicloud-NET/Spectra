namespace Spectra.Kitchen.Audio;

/// <summary>
/// A marker as a source declares it, before the cook has named, checked or
/// resampled it.
/// </summary>
/// <param name="Frame">Sample frames from the start, at the source's own rate. May be past the end.</param>
/// <param name="Label">The name the source gives it, or empty when it gives none.</param>
public readonly record struct SourceMarker(long Frame, string Label);
