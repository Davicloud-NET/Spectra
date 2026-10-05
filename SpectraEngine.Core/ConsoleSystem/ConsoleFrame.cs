using SpectraEngine.Core.Entities;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>What the commands on a line may reach while they run.</summary>
/// <param name="Scene">The active scene, or null when there is none.</param>
/// <param name="Entities">The running entity world, or null when the level is not running.</param>
/// <param name="IsPlaying">
/// Whether play mode is on. It can be on with no entity world, after the
/// level was replaced during play.
/// </param>
public readonly record struct ConsoleFrame(Scene.Scene? Scene, EntityWorld? Entities, bool IsPlaying = false);
