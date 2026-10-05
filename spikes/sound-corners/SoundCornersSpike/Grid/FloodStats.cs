namespace SoundCornersSpike.Grid;

// What one flood did, for the tables.
internal struct FloodStats
{
    public int Settled;
    public int Touched;
    public long HeapPushes;
    public int HeapPeak;
    public long LosChecks;
    public long LosSteps;
    public long ExactRays;
    public bool BudgetHit;
    public bool ExitedEarly;
    public int Seeds;
}
