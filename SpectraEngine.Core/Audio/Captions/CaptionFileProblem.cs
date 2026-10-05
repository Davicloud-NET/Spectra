namespace SpectraEngine.Core.Audio.Captions;

/// <summary>A line of a caption file that was not read as its writer meant.</summary>
/// <param name="Line">The line of the file, counted from one.</param>
/// <param name="Kind">What is wrong with it.</param>
/// <param name="Message">What is wrong with it, for a person. No file name and no full stop.</param>
public readonly record struct CaptionFileProblem(int Line, CaptionFileProblemKind Kind, string Message);
