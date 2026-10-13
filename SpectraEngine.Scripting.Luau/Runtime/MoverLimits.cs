using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What a script's mover may hand the engine. A number past these squares to
// no number in the capsule maths, which then throws inside the tick.
internal static class MoverLimits
{
    // Above what the caps of PlayerWrites add up to: the fastest walk with
    // a sprint, a throw across it and a fall. So the mover the engine ships
    // does not fail where the built-in one, which has no cap, goes on.
    public const float MaxSpeed = 4f * PlayerWrites.MaxSpeed;

    // How far one sweep goes, how far off a gather looks, and how far from
    // where the player began the tick either may start. More than a tick at
    // MaxSpeed, and no more: the collision builds all a mover reaches.
    public const float MaxReach = 1_000f;

    // How far from the origin a place may be on each axis.
    public const float MaxCoordinate = 1e9f;

    public const int MaxTicks = byte.MaxValue;

    // False for what is no number, too.
    public static bool IsPlace(Vector3 value) =>
        MathF.Abs(value.X) <= MaxCoordinate && MathF.Abs(value.Y) <= MaxCoordinate && MathF.Abs(value.Z) <= MaxCoordinate;

    public static bool IsInReach(Vector3 place, Vector3 start) => Vector3.Distance(place, start) <= MaxReach;

    public static bool IsWithin(Vector3 value, float most) => value.Length() <= most;

    public static bool IsFrom0To(float most, float value) => value >= 0f && value <= most;

    public static bool IsTicks(float value) => IsFrom0To(MaxTicks, value) && value == MathF.Floor(value);

    public static string NotAPlace(string what, Vector3 value) => string.Create(
        CultureInfo.InvariantCulture,
        $"{what} cannot be ({value.X}, {value.Y}, {value.Z}): it takes three numbers, each within a billion units of the origin.");

    public static string OutOfReach(string what, Vector3 value) => string.Create(
        CultureInfo.InvariantCulture,
        $"{what} cannot be ({value.X}, {value.Y}, {value.Z}): it is more than {MaxReach} units from where the player began the tick.");

    public static string TooLong(string what, Vector3 value, float most, string unit) => string.Create(
        CultureInfo.InvariantCulture,
        $"{what} cannot be ({value.X}, {value.Y}, {value.Z}): it takes three numbers and no more than {most} {unit}.");

    public static string NotADirection(Vector3 value) => string.Create(
        CultureInfo.InvariantCulture,
        $"A normal cannot be ({value.X}, {value.Y}, {value.Z}): it takes three numbers that are not all zero.");

    public static string OutOfRange(string what, float value, float most) => string.Create(
        CultureInfo.InvariantCulture, $"{what} cannot be {value}: it takes a number from 0 to {most}.");

    public static string NotTicks(string what, float value) => string.Create(
        CultureInfo.InvariantCulture, $"{what} cannot be {value}: it takes a whole number from 0 to {MaxTicks}.");
}
