namespace SpectraEngine.Core.Scene;

// What a node counts in its subtree, itself included. A subtree's counts are
// added to every ancestor in one walk, and taken away the same way.
internal struct SubtreeBrushCounts
{
    // Brushes of any kind. No scale may sit above one.
    public int Total;

    // World brushes on their nodes. Moving one costs a recompile.
    public int World;

    // World brushes a bake took off their nodes.
    public int Baked;

    // A world brush is one of Total, so it needs no test of its own.
    public readonly bool IsEmpty => Total == 0 && Baked == 0;

    public readonly SubtreeBrushCounts Negated => new() { Total = -Total, World = -World, Baked = -Baked };

    public void Add(in SubtreeBrushCounts delta)
    {
        Total += delta.Total;
        World += delta.World;
        Baked += delta.Baked;
    }
}
