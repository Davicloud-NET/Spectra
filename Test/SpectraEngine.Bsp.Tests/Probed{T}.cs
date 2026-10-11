namespace SpectraEngine.Bsp.Tests;

/// <summary>A value in the CFrame probe with the Luau expression that makes the same value in Roblox.</summary>
internal readonly record struct Probed<T>(string Luau, T Value);
