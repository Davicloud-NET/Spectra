namespace SoundCornersSpike.Grid;

internal enum FloodAlgorithm
{
    // Breadth first over the six face neighbours. Every step costs one cell.
    Bfs6,

    // Dijkstra over all 26 neighbours with their real step lengths.
    Dijkstra26,

    // Lazy Theta*: a cell takes its parent's parent when it can see it, so a
    // path is a few straight legs. Expands six neighbours.
    Theta6,

    // The same, expanding 26.
    Theta26,

    // First-order fast marching on the six-neighbour stencil.
    Marching,
}
