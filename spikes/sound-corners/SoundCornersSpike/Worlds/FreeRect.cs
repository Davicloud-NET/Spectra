namespace SoundCornersSpike.Worlds;

// A rectangle of open space in the plane a hand case is solved in.
internal readonly record struct FreeRect(float X0, float Y0, float X1, float Y1);
