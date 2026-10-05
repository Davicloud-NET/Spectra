using SpectraEngine.Core;

namespace SpectraEngine.Editor.Shell;

/// <summary>What the user is owed a line about after an engine session died.</summary>
/// <param name="Fault">Why the session died, or null when a restart was asked for by hand.</param>
/// <param name="Level">What became of the level.</param>
/// <param name="WasPlaying">Whether a run was stopped by it.</param>
/// <param name="Loss">What the kept level could not hold, as a sentence, or null.</param>
public sealed record SessionFaultNotice(EngineFault? Fault, LevelOutcome Level, bool WasPlaying, string? Loss);
