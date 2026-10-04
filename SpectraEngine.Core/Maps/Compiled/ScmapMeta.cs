using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// The 48-byte fixed preamble of the <c>META</c> section: the scene's metadata
/// and the constants the compile was run with. A load refuses the map when the
/// three float constants differ from the running engine's. The spawn array follows.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapMeta
{
    /// <summary>Index into <c>STRT</c> of the scene's name.</summary>
    public readonly uint SceneNameString;

    /// <summary>How many <see cref="ScmapSpawn"/> records follow this preamble.</summary>
    public readonly uint SpawnCount;

    /// <summary>The chunk cell size the world was compiled on. Must equal <c>ChunkCoord.CellSize</c>.</summary>
    public readonly float CellSize;

    /// <summary>The cross-cell weld band the compile used. Must equal <c>ChunkGrid.WeldBand</c>.</summary>
    public readonly float WeldBand;

    /// <summary>The vertex snap grid the compile used. Must equal <c>VertexSnapper.GridSize</c>.</summary>
    public readonly float SnapGrid;

    /// <summary>Cells per region edge. Zero: the region index is reserved and never written.</summary>
    public readonly uint RegionSize;

    /// <summary>Luau debug level the bytecode was compiled at. Zero until scripts are cooked.</summary>
    public readonly uint BytecodeDebugLevel;

    /// <summary>Cook switches that changed what was written. None defined in v1; written zero.</summary>
    public readonly uint CookFlags;

    /// <summary>Reserved; written zero.</summary>
    public readonly ulong Reserved0;

    /// <summary>Reserved; written zero.</summary>
    public readonly ulong Reserved1;

    /// <summary>
    /// Builds the preamble. The compile constants are stamped from the running engine.
    /// </summary>
    public ScmapMeta(uint sceneNameString, uint spawnCount, uint bytecodeDebugLevel = 0, uint cookFlags = 0)
    {
        SceneNameString = sceneNameString;
        SpawnCount = spawnCount;
        CellSize = ScmapFormat.EngineCellSize;
        WeldBand = ScmapFormat.EngineWeldBand;
        SnapGrid = ScmapFormat.EngineSnapGrid;
        RegionSize = 0;
        BytecodeDebugLevel = bytecodeDebugLevel;
        CookFlags = cookFlags;
        Reserved0 = 0;
        Reserved1 = 0;
    }
}
