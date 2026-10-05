using System.Numerics;

namespace SpectraEngine.Core.Play;

/// <summary>What one <see cref="PlaySession.Tick"/> did to the character, for whoever draws it.</summary>
/// <param name="PreviousPosition">Feet position before the tick, to blend from.</param>
/// <param name="Respawned">Whether the fall-out guard put the character back at its spawn.</param>
public readonly record struct PlayTickResult(Vector3 PreviousPosition, bool Respawned);
