using System.Numerics;

namespace SoundCornersSpike.Grid;

internal sealed class FloodOptions
{
    public FloodAlgorithm Algorithm { get; set; } = FloodAlgorithm.Theta6;

    // The longest path the flood follows, in its own measure.
    public float Radius { get; set; } = 40f;

    // How much longer than the true path the flood's own measure of a path
    // can be: 1 for Theta*, the square root of three for six-neighbour steps.
    // The reach bound divides by it, so it never drops a path that is in reach.
    public float Stretch { get; set; } = 1f;

    // The vertical limit, as world heights of cell centres.
    public float MinY { get; set; } = float.NegativeInfinity;

    public float MaxY { get; set; } = float.PositiveInfinity;

    // Stop after this many cells. Zero is no budget.
    public int CellBudget { get; set; }

    // Theta only: how a cell's sight of its parent is checked.
    public Sight Sight { get; set; } = Sight.Grid;

    public bool PartLinks { get; set; } = true;

    // The sounds whose straight line is blocked, for the two bounds below.
    public Vector3[] Targets { get; set; } = [];

    // Per target, the longest path worth finding: its max distance.
    public float[] TargetReach { get; set; } = [];

    // Stop once every target in the window has been reached.
    public bool EarlyExit { get; set; }

    // Do not expand a cell from which no target can still be within reach.
    public bool Prune { get; set; }
}
