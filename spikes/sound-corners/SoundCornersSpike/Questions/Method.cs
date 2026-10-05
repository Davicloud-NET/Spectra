using SoundCornersSpike.Grid;

namespace SoundCornersSpike.Questions;

// A flood and the way its answer is read.
// Sight: how a Theta* flood checks a parent. Pull: how the other floods'
// chains of parents are pulled straight when a sound is read.
internal readonly record struct Method(FloodAlgorithm Algorithm, Sight Sight, string Name, Sight Pull = Sight.Grid)
{
    // How far the flood's own measure can overstate a path. Six-neighbour
    // steps along a space diagonal take the square root of three times its
    // length. 26 neighbours and fast marching stay within about an eighth.
    public float Stretch => Algorithm switch
    {
        FloodAlgorithm.Bfs6 => 1.7321f,
        FloodAlgorithm.Dijkstra26 or FloodAlgorithm.Marching => 1.13f,
        _ => 1f,
    };

    /// <summary>Options for a flood that finds every path up to <paramref name="radius"/> long.</summary>
    public FloodOptions Options(float radius) =>
        new() { Algorithm = Algorithm, Sight = Sight, Radius = radius * Stretch, Stretch = Stretch };

    public PathReader Reader(Flood flood) => new(flood) { PullSight = Pull };
}
