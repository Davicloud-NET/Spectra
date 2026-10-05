using SpectraEngine.Core.Entities;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>What the commands on a line may reach while they run.</summary>
/// <param name="Scene">The active scene, or null when there is none.</param>
/// <param name="Entities">The running entity world, or null when the level is not running.</param>
public readonly record struct ConsoleFrame(Scene.Scene? Scene, EntityWorld? Entities);
