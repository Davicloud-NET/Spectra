using System;
using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Probes;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Grid;

// Which cells of the carved world are air, worked out on first use and kept
// until the world changes. Parts are not in here: they move.
internal sealed class AirField
{
    public const byte AirKnown = 1;
    public const byte Air = 2;
    public const byte LinksKnown = 4;
    public const byte LinkX = 8;
    public const byte LinkY = 16;
    public const byte LinkZ = 32;
    public const byte FullyOpen = Air | LinkX | LinkY | LinkZ;

    private const int BlockShift = 4;
    private const int BlockSize = 1 << BlockShift;
    private const int BlockMask = BlockSize - 1;
    private const int BlockCells = BlockSize * BlockSize * BlockSize;
    private const int KeyBias = 1 << 20;

    private readonly Dictionary<long, byte[]> _blocks = [];
    private readonly SolidProbe _probe;
    private long _lastKey = long.MinValue;
    private byte[]? _lastBlock;

    public AirField(SolidProbe probe, float cell, CellRule rule, Vector3 origin = default)
    {
        _probe = probe;
        Cell = cell;
        Rule = rule;
        Origin = origin;
    }

    public float Cell { get; }

    public CellRule Rule { get; }

    // Where cell (0, 0, 0) has its low corner. Not zero when the grid is
    // moved off the snap grid levels are built on.
    public Vector3 Origin { get; }

    public SolidProbe Probe => _probe;

    public long PointTests { get; private set; }

    public long BoxTests { get; private set; }

    public long SegmentTests { get; private set; }

    public long CellsClassified { get; private set; }

    public int Blocks => _blocks.Count;

    public int EmptyBlocks { get; private set; }

    public long MemoryBytes => (long)_blocks.Count * BlockCells;

    public Vector3 Center(int x, int y, int z) =>
        Origin + new Vector3((x + 0.5f) * Cell, (y + 0.5f) * Cell, (z + 0.5f) * Cell);

    public void CellOf(Vector3 point, out int x, out int y, out int z)
    {
        Vector3 local = (point - Origin) / Cell;
        x = (int)MathF.Floor(local.X);
        y = (int)MathF.Floor(local.Y);
        z = (int)MathF.Floor(local.Z);
    }

    public bool IsAir(int x, int y, int z) => (Flags(x, y, z) & Air) != 0;

    // The cell's air bit and its links to the three neighbours on the
    // positive side.
    public byte Flags(int x, int y, int z)
    {
        byte[] block = Block(x >> BlockShift, y >> BlockShift, z >> BlockShift);
        int slot = Slot(x, y, z);
        byte flags = block[slot];
        if ((flags & LinksKnown) != 0) return flags;

        bool air = AirOf(x, y, z);
        flags = (byte)(AirKnown | LinksKnown | (air ? Air : 0));

        if (air)
        {
            if (AirOf(x + 1, y, z) && LinkClear(x, y, z, x + 1, y, z)) flags |= LinkX;
            if (AirOf(x, y + 1, z) && LinkClear(x, y, z, x, y + 1, z)) flags |= LinkY;
            if (AirOf(x, y, z + 1) && LinkClear(x, y, z, x, y, z + 1)) flags |= LinkZ;
        }

        block[slot] = flags;
        return flags;
    }

    private bool AirOf(int x, int y, int z)
    {
        byte[] block = Block(x >> BlockShift, y >> BlockShift, z >> BlockShift);
        int slot = Slot(x, y, z);
        byte flags = block[slot];
        if ((flags & AirKnown) != 0) return (flags & Air) != 0;

        bool air = Classify(Center(x, y, z));
        CellsClassified++;
        block[slot] = (byte)(AirKnown | (air ? Air : 0));
        return air;
    }

    private bool Classify(Vector3 center)
    {
        switch (Rule)
        {
            case CellRule.Box:
                BoxTests++;
                return _probe.ClassifyBox(center, new Vector3(Cell * 0.5f * 0.999f)) == BoxContent.Air;

            case CellRule.NinePoints:
                PointTests++;
                if (_probe.IsSolid(center)) return false;

                float reach = Cell * 0.5f * 0.999f;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -reach : reach,
                        (corner & 2) == 0 ? -reach : reach,
                        (corner & 4) == 0 ? -reach : reach);

                    PointTests++;
                    if (_probe.IsSolid(center + offset)) return false;
                }

                return true;

            default:
                PointTests++;
                return !_probe.IsSolid(center);
        }
    }

    private bool LinkClear(int ax, int ay, int az, int bx, int by, int bz)
    {
        if (Rule != CellRule.CenterAndLinks) return true;

        SegmentTests++;
        return !_probe.SegmentBlocked(Center(ax, ay, az), Center(bx, by, bz));
    }

    private static int Slot(int x, int y, int z) =>
        ((z & BlockMask) << (2 * BlockShift)) | ((y & BlockMask) << BlockShift) | (x & BlockMask);

    private byte[] Block(int bx, int by, int bz)
    {
        long key = ((long)(bx + KeyBias) << 42) | ((long)(by + KeyBias) << 21) | (long)(bz + KeyBias);
        if (key == _lastKey) return _lastBlock!;

        if (!_blocks.TryGetValue(key, out byte[]? block))
        {
            block = new byte[BlockCells];

            // A block no brush reaches is air throughout, with no query.
            if (!TouchesGeometry(bx, by, bz))
            {
                Array.Fill(block, (byte)(AirKnown | LinksKnown | FullyOpen));
                EmptyBlocks++;
            }

            _blocks.Add(key, block);
        }

        _lastKey = key;
        _lastBlock = block;
        return block;
    }

    // One cell more on the positive side, where the block's outer links end.
    private bool TouchesGeometry(int bx, int by, int bz)
    {
        Vector3 min = Origin + (new Vector3(bx, by, bz) * (BlockSize * Cell));
        Vector3 max = min + new Vector3((BlockSize + 1) * Cell);

        ChunkCoord from = ChunkCoord.FromPosition(min);
        ChunkCoord to = ChunkCoord.FromPosition(max);

        for (int x = from.X; x <= to.X; x++)
        {
            for (int y = from.Y; y <= to.Y; y++)
            {
                for (int z = from.Z; z <= to.Z; z++)
                {
                    if (_probe.HasGeometry(new ChunkCoord(x, y, z))) return true;
                }
            }
        }

        return false;
    }
}
