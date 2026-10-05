namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// Something a subtitle file uses that the engine does not read. The words
/// around it are still shown.
/// </summary>
/// <param name="Line">The first line of the file that uses it, counted from one.</param>
/// <param name="What">What it is, to finish the sentence "The engine does not read".</param>
public readonly record struct SubtitleUnreadPart(int Line, string What);
