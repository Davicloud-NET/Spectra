using System;
using System.Numerics;
using SoundCornersSpike.Probes;

namespace SoundCornersSpike.Grid;

// The box of cells round the listener that one flood works in: its state
// arrays, which moves are open, and line of sight between cells.
internal sealed class FloodWindow
{
    // Parent values that are not a cell.
    public const int Root = -1;
    public const int None = -2;

    public const byte Queued = 1;
    public const byte Closed = 2;
    public const byte Validated = 4;
    public const byte TargetNear = 8;
    public const byte NearPart = 0x40;
    public const byte PartBlocked = 0x80;

    private int[] _touched = new int[1024];
    private int _touchedCount;
    private bool _startExempt;

    public float[] G { get; private set; } = [];

    public int[] Parent { get; private set; } = [];

    public int[] FirstHop { get; private set; } = [];

    public byte[] Flags { get; private set; } = [];

    public byte[] Mark { get; private set; } = [];

    public AirField Field { get; private set; } = null!;

    public PartSet Parts { get; private set; } = PartSet.Empty;

    // Whether a part also closes the links its hull crosses, not just the
    // cells whose centre it holds.
    public bool PartLinks { get; private set; }

    public Vector3 Listener { get; private set; }

    public int Nx { get; private set; }

    public int Ny { get; private set; }

    public int Nz { get; private set; }

    public int Ox { get; private set; }

    public int Oy { get; private set; }

    public int Oz { get; private set; }

    public int StrideY { get; private set; }

    public int StrideZ { get; private set; }

    public float Cell { get; private set; }

    public int CellCount => Nx * Ny * Nz;

    public int Touched => _touchedCount;

    public long LosChecks { get; private set; }

    public long LosSteps { get; private set; }

    public long ExactRays { get; private set; }

    // What the arrays cost for this window: G, Parent, FirstHop, Flags, Mark.
    public long MemoryBytes => (long)CellCount * (4 + 4 + 4 + 1 + 1);

    public void Setup(
        AirField field, PartSet parts, bool partLinks, Vector3 listener, float radius, float minY, float maxY)
    {
        Reset();

        Field = field;
        Parts = parts;
        PartLinks = partLinks;
        Listener = listener;
        Cell = field.Cell;
        LosChecks = 0;
        LosSteps = 0;
        ExactRays = 0;

        field.CellOf(listener, out int lx, out int ly, out int lz);
        int half = (int)MathF.Ceiling(radius / Cell) + 2;

        // The vertical limit, never tighter than the cells the seeds need.
        int low = ly - half;
        int high = ly + half;
        if (!float.IsNegativeInfinity(minY))
            low = Math.Max(low, Math.Min(ly - 1, (int)MathF.Floor((minY - field.Origin.Y) / Cell)));
        if (!float.IsPositiveInfinity(maxY))
            high = Math.Min(high, Math.Max(ly + 1, (int)MathF.Floor((maxY - field.Origin.Y) / Cell)));

        Ox = lx - half;
        Oz = lz - half;
        Oy = low - 1;
        Nx = (2 * half) + 1;
        Nz = Nx;
        Ny = high - low + 3;
        StrideY = Nx;
        StrideZ = Nx * Ny;

        int cells = CellCount;
        if (G.Length < cells)
        {
            G = new float[cells];
            Array.Fill(G, float.PositiveInfinity);
            Parent = new int[cells];
            FirstHop = new int[cells];
            Flags = new byte[cells];
            Mark = new byte[cells];
        }

        MarkParts();

        _startExempt = !Passable(Index(lx - Ox, ly - Oy, lz - Oz), lx - Ox, ly - Oy, lz - Oz);
    }

    public int Index(int x, int y, int z) => x + (StrideY * y) + (StrideZ * z);

    public void Decode(int index, out int x, out int y, out int z)
    {
        z = index / StrideZ;
        int rest = index - (z * StrideZ);
        y = rest / StrideY;
        x = rest - (y * StrideY);
    }

    public bool InRange(int x, int y, int z) =>
        (uint)(x - 1) < (uint)(Nx - 2) && (uint)(y - 1) < (uint)(Ny - 2) && (uint)(z - 1) < (uint)(Nz - 2);

    public Vector3 CenterWorld(int x, int y, int z) => Field.Center(Ox + x, Oy + y, Oz + z);

    public Vector3 CenterWorld(int index)
    {
        Decode(index, out int x, out int y, out int z);
        return CenterWorld(x, y, z);
    }

    // False outside the window.
    public bool TryLocalCell(Vector3 world, out int x, out int y, out int z)
    {
        Field.CellOf(world, out int wx, out int wy, out int wz);
        x = wx - Ox;
        y = wy - Oy;
        z = wz - Oz;
        return InRange(x, y, z);
    }

    public void SetMark(int index, byte bits)
    {
        if ((Flags[index] | Mark[index]) == 0) Touch(index);
        Mark[index] |= bits;
    }

    public byte FlagsAt(int index, int x, int y, int z)
    {
        byte flags = Flags[index];
        if (flags != 0) return flags;

        flags = Field.Flags(Ox + x, Oy + y, Oz + z);
        if (Mark[index] == 0) Touch(index);
        Flags[index] = flags;
        return flags;
    }

    public bool Passable(int index, int x, int y, int z) =>
        (FlagsAt(index, x, y, z) & AirField.Air) != 0 && (Mark[index] & PartBlocked) == 0;

    // A step along one axis from a cell the caller knows is passable.
    public bool AxisOpen(int index, int x, int y, int z, int axis, int direction)
    {
        int nx = x, ny = y, nz = z, nIndex;
        switch (axis)
        {
            case 0: nx += direction; nIndex = index + direction; break;
            case 1: ny += direction; nIndex = index + (direction * StrideY); break;
            default: nz += direction; nIndex = index + (direction * StrideZ); break;
        }

        if (!Passable(nIndex, nx, ny, nz)) return false;

        // The link is kept on the cell at the low end.
        byte low = direction > 0 ? FlagsAt(index, x, y, z) : Flags[nIndex];
        if ((low & (AirField.LinkX << axis)) == 0) return false;

        if (PartLinks && ((Mark[index] | Mark[nIndex]) & NearPart) != 0)
            return !Parts.SegmentBlocked(CenterWorld(x, y, z), CenterWorld(nx, ny, nz));

        return true;
    }

    // A step to any of the 26 neighbours. A diagonal is open only when every
    // way round it along the axes is open, so it never cuts a corner.
    public bool StepOpen(int index, int x, int y, int z, int dx, int dy, int dz)
    {
        int moved = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) + (dz != 0 ? 1 : 0);

        if (moved == 1)
        {
            if (dx != 0) return AxisOpen(index, x, y, z, 0, dx);
            if (dy != 0) return AxisOpen(index, x, y, z, 1, dy);
            return AxisOpen(index, x, y, z, 2, dz);
        }

        if (moved == 2)
        {
            if (dz == 0) return FaceOpen(index, x, y, z, 0, dx, 1, dy);
            if (dy == 0) return FaceOpen(index, x, y, z, 0, dx, 2, dz);
            return FaceOpen(index, x, y, z, 1, dy, 2, dz);
        }

        return FaceOpen(index, x, y, z, 0, dx, 1, dy)
            && FaceOpen(index, x, y, z, 0, dx, 2, dz)
            && FaceOpen(index, x, y, z, 1, dy, 2, dz)
            && AxisOpen(Index(x + dx, y + dy, z), x + dx, y + dy, z, 2, dz)
            && AxisOpen(Index(x + dx, y, z + dz), x + dx, y, z + dz, 1, dy)
            && AxisOpen(Index(x, y + dy, z + dz), x, y + dy, z + dz, 0, dx);
    }

    // True when the cell and its 26 neighbours are air with every link open
    // and no part near. Then all 26 steps are open and none needs checking.
    public bool NeighborhoodOpen(int index, int x, int y, int z)
    {
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int row = index + (dy * StrideY) + (dz * StrideZ);
                for (int dx = -1; dx <= 1; dx++)
                {
                    int n = row + dx;
                    if ((FlagsAt(n, x + dx, y + dy, z + dz) & AirField.FullyOpen) != AirField.FullyOpen) return false;
                    if ((Mark[n] & (NearPart | PartBlocked)) != 0) return false;
                }
            }
        }

        return true;
    }

    // Straight line from the listener to a cell's centre, against the real
    // world and the parts. Not the grid.
    public bool ExactRootLos(int x, int y, int z)
    {
        ExactRays++;
        Vector3 center = CenterWorld(x, y, z);
        return !Field.Probe.SegmentBlocked(Listener, center) && !Parts.SegmentBlocked(Listener, center);
    }

    // Straight line between a parent (a cell or the listener) and a cell's
    // centre, against the real world and the parts.
    public bool ExactLos(int parent, int x, int y, int z)
    {
        if (parent == Root) return ExactRootLos(x, y, z);

        ExactRays++;
        Vector3 from = CenterWorld(parent);
        Vector3 to = CenterWorld(x, y, z);
        return !Field.Probe.SegmentBlocked(from, to) && !Parts.SegmentBlocked(from, to);
    }

    public bool Sees(Sight sight, int parent, int x, int y, int z) => sight switch
    {
        Sight.Ray => ExactLos(parent, x, y, z),
        Sight.RayFromListener when parent == Root => ExactRootLos(x, y, z),
        _ => GridLos(parent, x, y, z),
    };

    // Line of sight on the grid from a parent (a cell or the listener) to a
    // cell's centre: every cell crossed is passable and every face crossed is
    // an open step.
    public bool GridLos(int parent, int x, int y, int z)
    {
        LosChecks++;

        if (parent == Root)
        {
            Vector3 local = ((Listener - Field.Origin) / Cell) - new Vector3(Ox, Oy, Oz);
            return Walk(
                local, (int)MathF.Floor(local.X), (int)MathF.Floor(local.Y), (int)MathF.Floor(local.Z),
                x, y, z, _startExempt);
        }

        Decode(parent, out int px, out int py, out int pz);
        return Walk(new Vector3(px + 0.5f, py + 0.5f, pz + 0.5f), px, py, pz, x, y, z, exemptStart: false);
    }

    private bool Walk(Vector3 from, int ax, int ay, int az, int bx, int by, int bz, bool exemptStart)
    {
        if (ax == bx && ay == by && az == bz) return true;

        float dx = bx + 0.5f - from.X;
        float dy = by + 0.5f - from.Y;
        float dz = bz + 0.5f - from.Z;

        Axis(ax, bx, from.X, dx, out int sx, out float tx, out float stepX);
        Axis(ay, by, from.Y, dy, out int sy, out float ty, out float stepY);
        Axis(az, bz, from.Z, dz, out int sz, out float tz, out float stepZ);

        int x = ax, y = ay, z = az;
        int index = Index(x, y, z);
        int guard = Math.Abs(bx - ax) + Math.Abs(by - ay) + Math.Abs(bz - az) + 2;

        // Crossings closer together than this are taken as one diagonal step.
        const float Tie = 1e-5f;

        while (guard-- > 0)
        {
            float t = MathF.Min(tx, MathF.Min(ty, tz));
            int mx = tx - t <= Tie ? sx : 0;
            int my = ty - t <= Tie ? sy : 0;
            int mz = tz - t <= Tie ? sz : 0;

            if (exemptStart)
            {
                // The listener stands in a cell whose centre is in solid.
                // Only the cell stepped into is asked.
                exemptStart = false;
                if (!Passable(Index(x + mx, y + my, z + mz), x + mx, y + my, z + mz)) return false;
            }
            else if (!StepOpen(index, x, y, z, mx, my, mz))
            {
                return false;
            }

            x += mx;
            y += my;
            z += mz;
            index = Index(x, y, z);
            LosSteps++;

            // The end is a cell centre, so a line never leaves the end's
            // slab once it is in it. Rounding must not step it on.
            if (mx != 0) tx = x == bx ? float.PositiveInfinity : tx + stepX;
            if (my != 0) ty = y == by ? float.PositiveInfinity : ty + stepY;
            if (mz != 0) tz = z == bz ? float.PositiveInfinity : tz + stepZ;

            if (x == bx && y == by && z == bz) return true;
        }

        return false;
    }

    private static void Axis(int a, int b, float from, float delta, out int sign, out float t, out float step)
    {
        if (a == b || delta == 0f)
        {
            sign = 0;
            t = float.PositiveInfinity;
            step = float.PositiveInfinity;
            return;
        }

        sign = delta > 0f ? 1 : -1;
        float edge = sign > 0 ? a + 1 : a;
        t = (edge - from) / delta;
        step = sign / delta;
    }

    private bool FaceOpen(int index, int x, int y, int z, int axisA, int stepA, int axisB, int stepB)
    {
        if (!AxisOpen(index, x, y, z, axisA, stepA) || !AxisOpen(index, x, y, z, axisB, stepB)) return false;

        Shift(index, x, y, z, axisA, stepA, out int ia, out int xa, out int ya, out int za);
        Shift(index, x, y, z, axisB, stepB, out int ib, out int xb, out int yb, out int zb);

        return AxisOpen(ia, xa, ya, za, axisB, stepB) && AxisOpen(ib, xb, yb, zb, axisA, stepA);
    }

    private void Shift(
        int index, int x, int y, int z, int axis, int step, out int nIndex, out int nx, out int ny, out int nz)
    {
        nx = x;
        ny = y;
        nz = z;

        switch (axis)
        {
            case 0: nx += step; nIndex = index + step; break;
            case 1: ny += step; nIndex = index + (step * StrideY); break;
            default: nz += step; nIndex = index + (step * StrideZ); break;
        }
    }

    // Blocks the cells whose centre a part holds, and flags the cells near a
    // part so their steps are tested against its hull.
    private void MarkParts()
    {
        for (int i = 0; i < Parts.Count; i++)
        {
            PartHull hull = Parts.Hulls[i];
            Field.CellOf(hull.Bounds.Min, out int x0, out int y0, out int z0);
            Field.CellOf(hull.Bounds.Max, out int x1, out int y1, out int z1);

            x0 = Math.Max(x0 - 1 - Ox, 0);
            y0 = Math.Max(y0 - 1 - Oy, 0);
            z0 = Math.Max(z0 - 1 - Oz, 0);
            x1 = Math.Min(x1 + 1 - Ox, Nx - 1);
            y1 = Math.Min(y1 + 1 - Oy, Ny - 1);
            z1 = Math.Min(z1 + 1 - Oz, Nz - 1);

            for (int z = z0; z <= z1; z++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        byte bits = hull.Contains(CenterWorld(x, y, z)) ? (byte)(NearPart | PartBlocked) : NearPart;
                        SetMark(Index(x, y, z), bits);
                    }
                }
            }
        }
    }

    private void Touch(int index)
    {
        if (_touchedCount == _touched.Length) Array.Resize(ref _touched, _touched.Length * 2);
        _touched[_touchedCount++] = index;
    }

    private void Reset()
    {
        for (int i = 0; i < _touchedCount; i++)
        {
            int index = _touched[i];
            G[index] = float.PositiveInfinity;
            Flags[index] = 0;
            Mark[index] = 0;
        }

        _touchedCount = 0;
    }
}
