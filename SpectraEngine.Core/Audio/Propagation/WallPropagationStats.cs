namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>What <see cref="WallPropagation"/> did on its last call.</summary>
/// <param name="Sounds">Sounds loud enough at their distance for walls to matter.</param>
/// <param name="Traces">Lines traced.</param>
/// <param name="Refreshed">Sounds that got a new answer.</param>
/// <param name="Waiting">Sounds whose answer is due and that have to wait for a later frame.</param>
/// <param name="Unanswered">
/// New sounds among those waiting. They have no answer yet and are silent
/// until they get one.
/// </param>
public readonly record struct WallPropagationStats(
    int Sounds, int Traces, int Refreshed, int Waiting, int Unanswered);
