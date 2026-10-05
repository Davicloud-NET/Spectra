using System;
using System.Numerics;
using SoundCornersSpike.Probes;

namespace SoundCornersSpike.Grid;

// One flood outwards from the listener. Afterwards Window holds, for every
// cell reached, the length of its path and the cell before it.
// Long for one class: the four algorithms share the seeds, the bounds and the
// bookkeeping, and reading them side by side is the point of the spike.
internal sealed class Flood
{
    // The six face neighbours first, then the twelve edge and eight corner ones.
    private static readonly sbyte[] Dx = new sbyte[26];
    private static readonly sbyte[] Dy = new sbyte[26];
    private static readonly sbyte[] Dz = new sbyte[26];
    private static readonly float[] StepLength = new float[26];

    private readonly FloodHeap _heap = new();
    private readonly int[] _seedCells = new int[27];
    private readonly float[] _seedDistances = new float[27];
    private int[] _queue = new int[4096];
    private int _seedCount;

    private int[] _targetX = [];
    private int[] _targetY = [];
    private int[] _targetZ = [];
    private bool[] _targetOpen = [];
    private int _targetsOpen;
    private float _exitG;

    private FloodOptions _options = new();
    private FloodStats _stats;

    static Flood()
    {
        int k = 0;
        for (int moved = 1; moved <= 3; moved++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) != moved) continue;

                        Dx[k] = (sbyte)dx;
                        Dy[k] = (sbyte)dy;
                        Dz[k] = (sbyte)dz;
                        StepLength[k] = MathF.Sqrt(moved);
                        k++;
                    }
                }
            }
        }
    }

    public FloodWindow Window { get; } = new();

    public FloodAlgorithm Algorithm { get; private set; }

    public FloodStats Run(AirField field, PartSet parts, Vector3 listener, FloodOptions options)
    {
        _options = options;
        _stats = default;
        Algorithm = options.Algorithm;
        _heap.Clear();

        Window.Setup(field, parts, options.PartLinks, listener, options.Radius, options.MinY, options.MaxY);
        PrepareTargets();
        CollectSeeds();

        switch (options.Algorithm)
        {
            case FloodAlgorithm.Bfs6: RunBreadthFirst(); break;
            case FloodAlgorithm.Dijkstra26: RunDijkstra(); break;
            case FloodAlgorithm.Theta6: RunTheta(neighbors: 6); break;
            case FloodAlgorithm.Theta26: RunTheta(neighbors: 26); break;
            default: RunMarching(); break;
        }

        _stats.Seeds = _seedCount;
        _stats.Touched = Window.Touched;
        _stats.HeapPushes = _heap.Pushes;
        _stats.HeapPeak = _heap.Peak;
        _stats.LosChecks = Window.LosChecks;
        _stats.LosSteps = Window.LosSteps;
        _stats.ExactRays = Window.ExactRays;
        return _stats;
    }

    private void PrepareTargets()
    {
        FloodWindow w = Window;
        int count = _options.Targets.Length;
        if (_targetX.Length < count)
        {
            _targetX = new int[count];
            _targetY = new int[count];
            _targetZ = new int[count];
            _targetOpen = new bool[count];
        }

        _targetsOpen = 0;
        _exitG = 0f;

        for (int i = 0; i < count; i++)
        {
            _targetOpen[i] = w.TryLocalCell(_options.Targets[i], out _targetX[i], out _targetY[i], out _targetZ[i]);
            if (!_targetOpen[i]) continue;

            _targetsOpen++;
            for (int k = -1; k < 26; k++)
            {
                int x = _targetX[i] + (k < 0 ? 0 : Dx[k]);
                int y = _targetY[i] + (k < 0 ? 0 : Dy[k]);
                int z = _targetZ[i] + (k < 0 ? 0 : Dz[k]);
                if (w.InRange(x, y, z)) w.SetMark(w.Index(x, y, z), FloodWindow.TargetNear);
            }
        }
    }

    // The cells round the listener it can reach in a straight line, checked
    // against the real world. The listener's own cell need not be air.
    private void CollectSeeds()
    {
        FloodWindow w = Window;
        _seedCount = 0;
        if (!w.TryLocalCell(w.Listener, out int cx, out int cy, out int cz)) return;

        for (int k = -1; k < 26; k++)
        {
            int x = cx + (k < 0 ? 0 : Dx[k]);
            int y = cy + (k < 0 ? 0 : Dy[k]);
            int z = cz + (k < 0 ? 0 : Dz[k]);
            if (!w.InRange(x, y, z)) continue;

            int index = w.Index(x, y, z);
            if (!w.Passable(index, x, y, z) || !w.ExactRootLos(x, y, z)) continue;

            _seedCells[_seedCount] = index;
            _seedDistances[_seedCount] = Vector3.Distance(w.Listener, w.CenterWorld(x, y, z));
            _seedCount++;
        }
    }

    // True when the flood should stop.
    private bool Close(int cell, int x, int y, int z, float g)
    {
        FloodWindow w = Window;
        _stats.Settled++;

        if ((w.Mark[cell] & FloodWindow.TargetNear) != 0) NoteTargets(x, y, z, g);

        if (_options.CellBudget > 0 && _stats.Settled >= _options.CellBudget)
        {
            _stats.BudgetHit = true;
            return true;
        }

        if (_options.EarlyExit && _targetsOpen == 0 && g > _exitG)
        {
            _stats.ExitedEarly = true;
            return true;
        }

        return false;
    }

    // A target counts as reached when a cell beside it that can see it has
    // closed. The cell on the far side of a thin wall cannot.
    private void NoteTargets(int x, int y, int z, float g)
    {
        FloodWindow w = Window;

        for (int i = 0; i < _options.Targets.Length; i++)
        {
            if (!_targetOpen[i]) continue;
            if (Math.Abs(x - _targetX[i]) > 1 || Math.Abs(y - _targetY[i]) > 1 || Math.Abs(z - _targetZ[i]) > 1)
                continue;

            bool own = x == _targetX[i] && y == _targetY[i] && z == _targetZ[i];
            if (!own && w.Field.Probe.SegmentBlocked(w.CenterWorld(x, y, z), _options.Targets[i])) continue;

            _targetOpen[i] = false;
            _targetsOpen--;

            // A few cells more, so a better neighbour of the target closes too.
            _exitG = MathF.Max(_exitG, g + (3.5f * w.Cell));
        }
    }

    private bool Pruned(int x, int y, int z, float g)
    {
        if (!_options.Prune) return false;

        Vector3 center = Window.CenterWorld(x, y, z);
        for (int i = 0; i < _options.Targets.Length; i++)
        {
            if ((g / _options.Stretch) + Vector3.Distance(center, _options.Targets[i]) <= _options.TargetReach[i] + Window.Cell)
                return false;
        }

        return true;
    }

    private void RunBreadthFirst()
    {
        FloodWindow w = Window;
        float[] g = w.G;
        int head = 0, tail = 0;

        // A plain breadth-first starts from one cell. When the listener's
        // own cell is not air, its seeds all start at zero.
        w.TryLocalCell(w.Listener, out int cx, out int cy, out int cz);
        int own = w.Index(cx, cy, cz);
        bool ownIsSeed = false;
        for (int i = 0; i < _seedCount; i++) ownIsSeed |= _seedCells[i] == own;

        for (int i = 0; i < _seedCount; i++)
        {
            int seed = _seedCells[i];
            if (ownIsSeed && seed != own) continue;

            g[seed] = 0f;
            w.Parent[seed] = FloodWindow.Root;
            w.Mark[seed] |= FloodWindow.Closed;
            Enqueue(ref tail, seed);
        }

        while (head < tail)
        {
            int c = _queue[head++];
            w.Decode(c, out int x, out int y, out int z);
            float here = g[c];

            if (Close(c, x, y, z, here)) return;
            if (Pruned(x, y, z, here)) continue;

            float next = here + w.Cell;
            if (next > _options.Radius) continue;

            for (int k = 0; k < 6; k++)
            {
                int nx = x + Dx[k], ny = y + Dy[k], nz = z + Dz[k];
                if (!w.InRange(nx, ny, nz)) continue;

                int n = c + Dx[k] + (Dy[k] * w.StrideY) + (Dz[k] * w.StrideZ);
                if ((w.Mark[n] & FloodWindow.Closed) != 0) continue;
                if (!w.StepOpen(c, x, y, z, Dx[k], Dy[k], Dz[k])) continue;

                g[n] = next;
                w.Parent[n] = c;
                w.Mark[n] |= FloodWindow.Closed;
                Enqueue(ref tail, n);
            }
        }
    }

    private void Enqueue(ref int tail, int cell)
    {
        if (tail == _queue.Length) Array.Resize(ref _queue, tail * 2);
        _queue[tail++] = cell;
    }

    private void PushSeeds()
    {
        FloodWindow w = Window;
        _heap.Clear();

        for (int i = 0; i < _seedCount; i++)
        {
            int seed = _seedCells[i];
            w.G[seed] = _seedDistances[i];
            w.Parent[seed] = FloodWindow.Root;
            w.Mark[seed] |= FloodWindow.Queued | FloodWindow.Validated;
            _heap.Push(_seedDistances[i], seed);
        }
    }

    private void RunDijkstra()
    {
        FloodWindow w = Window;
        float[] g = w.G;
        PushSeeds();

        while (_heap.Count > 0)
        {
            int c = _heap.Pop(out float key);
            if ((w.Mark[c] & FloodWindow.Closed) != 0 || key != g[c]) continue;

            w.Mark[c] |= FloodWindow.Closed;
            w.Decode(c, out int x, out int y, out int z);

            if (Close(c, x, y, z, key)) return;
            if (Pruned(x, y, z, key)) continue;

            bool allOpen = w.NeighborhoodOpen(c, x, y, z);

            for (int k = 0; k < 26; k++)
            {
                int nx = x + Dx[k], ny = y + Dy[k], nz = z + Dz[k];
                if (!w.InRange(nx, ny, nz)) continue;

                int n = c + Dx[k] + (Dy[k] * w.StrideY) + (Dz[k] * w.StrideZ);
                if ((w.Mark[n] & FloodWindow.Closed) != 0) continue;

                float candidate = key + (StepLength[k] * w.Cell);
                if (candidate >= g[n] || candidate > _options.Radius) continue;
                if (!allOpen && !w.StepOpen(c, x, y, z, Dx[k], Dy[k], Dz[k])) continue;

                g[n] = candidate;
                w.Parent[n] = c;
                _heap.Push(candidate, n);
            }
        }
    }

    private void RunTheta(int neighbors)
    {
        FloodWindow w = Window;
        float[] g = w.G;
        Vector3 listenerLocal = ((w.Listener - w.Field.Origin) / w.Cell) - new Vector3(w.Ox, w.Oy, w.Oz);
        PushSeeds();

        while (_heap.Count > 0)
        {
            int c = _heap.Pop(out float key);
            byte mark = w.Mark[c];
            if ((mark & FloodWindow.Closed) != 0 || key != g[c]) continue;

            w.Decode(c, out int x, out int y, out int z);

            // The parent was taken on trust when this cell was queued. Check
            // it now, once, and fall back to a closed neighbour if it fails.
            if ((mark & FloodWindow.Validated) == 0)
            {
                w.Mark[c] |= FloodWindow.Validated;
                int trusted = w.Parent[c];

                if (!w.Sees(_options.Sight, trusted, x, y, z))
                {
                    if (FallBack(c, x, y, z, neighbors, trusted)) _heap.Push(g[c], c);
                    continue;
                }
            }

            w.Mark[c] |= FloodWindow.Closed;
            int parent = w.Parent[c];
            w.FirstHop[c] = parent == FloodWindow.Root ? c : w.FirstHop[parent];

            if (Close(c, x, y, z, key)) return;
            if (Pruned(x, y, z, key)) continue;

            Vector3 parentLocal = listenerLocal;
            float parentG = 0f;
            if (parent != FloodWindow.Root)
            {
                w.Decode(parent, out int px, out int py, out int pz);
                parentLocal = new Vector3(px + 0.5f, py + 0.5f, pz + 0.5f);
                parentG = g[parent];
            }

            bool allOpen = neighbors == 26 && w.NeighborhoodOpen(c, x, y, z);

            for (int k = 0; k < neighbors; k++)
            {
                int nx = x + Dx[k], ny = y + Dy[k], nz = z + Dz[k];
                if (!w.InRange(nx, ny, nz)) continue;

                int n = c + Dx[k] + (Dy[k] * w.StrideY) + (Dz[k] * w.StrideZ);
                if ((w.Mark[n] & FloodWindow.Closed) != 0) continue;

                var center = new Vector3(nx + 0.5f, ny + 0.5f, nz + 0.5f);
                float candidate = parentG + (Vector3.Distance(parentLocal, center) * w.Cell);
                if (candidate >= g[n] || candidate > _options.Radius) continue;
                if (!allOpen && !w.StepOpen(c, x, y, z, Dx[k], Dy[k], Dz[k])) continue;

                g[n] = candidate;
                w.Parent[n] = parent;
                w.Mark[n] = (byte)((w.Mark[n] & ~FloodWindow.Validated) | FloodWindow.Queued);
                _heap.Push(candidate, n);
            }
        }
    }

    // Gives the cell a parent it can really reach: the best closed neighbour,
    // or a closed neighbour's parent when the cell sees that and it is shorter.
    // False when it has none, and then the cell waits to be reached again.
    private bool FallBack(int c, int x, int y, int z, int neighbors, int failed)
    {
        FloodWindow w = Window;
        float best = float.PositiveInfinity;
        int bestCell = FloodWindow.None;
        Span<int> closed = stackalloc int[26];
        int closedCount = 0;

        for (int k = 0; k < neighbors; k++)
        {
            int nx = x + Dx[k], ny = y + Dy[k], nz = z + Dz[k];
            if (!w.InRange(nx, ny, nz)) continue;

            int n = c + Dx[k] + (Dy[k] * w.StrideY) + (Dz[k] * w.StrideZ);
            if ((w.Mark[n] & FloodWindow.Closed) == 0) continue;
            if (!w.StepOpen(c, x, y, z, Dx[k], Dy[k], Dz[k])) continue;

            closed[closedCount++] = n;
            float candidate = w.G[n] + (StepLength[k] * w.Cell);
            if (candidate >= best) continue;

            best = candidate;
            bestCell = n;
        }

        Vector3 center = w.CenterWorld(x, y, z);
        int tried = failed;

        for (int i = 0; i < closedCount; i++)
        {
            int parent = w.Parent[closed[i]];
            if (parent == tried || parent == failed) continue;

            float candidate = parent == FloodWindow.Root
                ? Vector3.Distance(w.Listener, center)
                : w.G[parent] + Vector3.Distance(w.CenterWorld(parent), center);
            if (candidate >= best) continue;

            tried = parent;
            if (!w.Sees(_options.Sight, parent, x, y, z)) continue;

            best = candidate;
            bestCell = parent;
        }

        w.G[c] = best;
        w.Parent[c] = bestCell;
        return bestCell != FloodWindow.None && best <= _options.Radius;
    }

    private void RunMarching()
    {
        FloodWindow w = Window;
        float[] g = w.G;
        PushSeeds();

        while (_heap.Count > 0)
        {
            int c = _heap.Pop(out float key);
            if ((w.Mark[c] & FloodWindow.Closed) != 0 || key != g[c]) continue;

            w.Mark[c] |= FloodWindow.Closed;
            w.Decode(c, out int x, out int y, out int z);

            if (Close(c, x, y, z, key)) return;
            if (Pruned(x, y, z, key)) continue;

            for (int k = 0; k < 6; k++)
            {
                int nx = x + Dx[k], ny = y + Dy[k], nz = z + Dz[k];
                if (!w.InRange(nx, ny, nz)) continue;

                int n = c + Dx[k] + (Dy[k] * w.StrideY) + (Dz[k] * w.StrideZ);
                if ((w.Mark[n] & FloodWindow.Closed) != 0) continue;
                if (!w.StepOpen(c, x, y, z, Dx[k], Dy[k], Dz[k])) continue;

                float arrival = Arrival(n, nx, ny, nz, out int from);
                if (arrival >= g[n] || arrival > _options.Radius) continue;

                g[n] = arrival;
                w.Parent[n] = from;
                _heap.Push(arrival, n);
            }
        }
    }

    // The upwind solution of the eikonal equation at a cell, from the closed
    // neighbours it can step to. from is the earliest of them.
    private float Arrival(int n, int x, int y, int z, out int from)
    {
        FloodWindow w = Window;
        Span<float> lowest = stackalloc float[3];
        from = FloodWindow.None;
        float earliest = float.PositiveInfinity;

        for (int axis = 0; axis < 3; axis++)
        {
            lowest[axis] = float.PositiveInfinity;

            for (int direction = -1; direction <= 1; direction += 2)
            {
                int stride = axis == 0 ? 1 : axis == 1 ? w.StrideY : w.StrideZ;
                int side = n + (direction * stride);

                int sx = x + (axis == 0 ? direction : 0);
                int sy = y + (axis == 1 ? direction : 0);
                int sz = z + (axis == 2 ? direction : 0);
                if (!w.InRange(sx, sy, sz)) continue;

                if ((w.Mark[side] & FloodWindow.Closed) == 0) continue;
                if (w.G[side] >= lowest[axis]) continue;
                if (!w.AxisOpen(n, x, y, z, axis, direction)) continue;

                lowest[axis] = w.G[side];
                if (w.G[side] < earliest)
                {
                    earliest = w.G[side];
                    from = side;
                }
            }
        }

        float a = lowest[0], b = lowest[1], c = lowest[2];
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);

        float h = w.Cell;
        float t = a + h;
        if (t <= b) return t;

        t = (a + b + MathF.Sqrt((2f * h * h) - ((a - b) * (a - b)))) * 0.5f;
        if (t <= c) return t;

        float sum = a + b + c;
        float discriminant = (sum * sum) - (3f * ((a * a) + (b * b) + (c * c) - (h * h)));
        return discriminant < 0f ? t : (sum + MathF.Sqrt(discriminant)) / 3f;
    }
}
