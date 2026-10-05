using System.Numerics;

namespace SoundCornersSpike.Worlds;

// One of the demo's point_sound entities.
// OwnPart: the part it sits under, as an index into the world's parts, or -1.
internal readonly record struct DemoSound(string Name, Vector3 Position, float MinDistance, float MaxDistance, int OwnPart);
