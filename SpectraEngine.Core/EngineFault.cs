namespace SpectraEngine.Core;

/// <summary>Why an engine's render thread ended on an exception.</summary>
/// <param name="Message">The exception's message, for a log or a details line.</param>
/// <param name="IsDeviceLoss">
/// Whether the graphics device was removed or reset. The scene is intact
/// then, and a new engine can carry on from it.
/// </param>
public sealed record EngineFault(string Message, bool IsDeviceLoss);
