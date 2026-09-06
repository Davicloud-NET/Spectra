namespace SpectraEngine.Core.Graphics;

/// <summary>Mesh-buffer bytes separated by GPU lifetime, captured on the render thread.</summary>
public readonly record struct MeshBufferMemory(ulong Active, ulong Retired, ulong Pooled);
