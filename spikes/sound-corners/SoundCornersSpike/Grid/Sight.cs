namespace SoundCornersSpike.Grid;

// How line of sight between two points of a path is checked.
internal enum Sight
{
    // Along the cells between the two, by their air bits and links.
    Grid,

    // With a ray against the real world when the parent is the listener, and
    // along the grid otherwise.
    RayFromListener,

    // With a ray against the real world, always.
    Ray,
}
