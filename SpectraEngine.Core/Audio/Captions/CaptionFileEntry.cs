namespace SpectraEngine.Core.Audio.Captions;

/// <summary>One sound's caption in a <see cref="CaptionFile"/>.</summary>
/// <param name="Sound">The sound's content path, normalized.</param>
/// <param name="Text">The words. A line break in them is one the writer chose.</param>
/// <param name="Line">The line of the file it was read from, counted from one.</param>
public readonly record struct CaptionFileEntry(string Sound, string Text, int Line);
