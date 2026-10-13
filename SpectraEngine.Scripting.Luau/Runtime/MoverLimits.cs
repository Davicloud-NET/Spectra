using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What a script's mover may hand the engine. A number past these squares to
// no number in the capsule maths, which then throws inside the tick.
internal static class MoverLimits
{
    // As fast as any script may make the player.
    public const float MaxSpeed = PlayerWrites.MaxSpeed;

    // How far one sweep goes, and how far off a gather looks.
    public const float MaxReach = 10_000f;

    // How far from the origin a place may be on each axis.
    public const float MaxCoordinate = 1e9f;

    public const int MaxTicks = byte.MaxValue;

    // False for what is no number, too.
    public static bool IsPlace(Vector3 value) =>
        MathF.Abs(value.X) <= MaxCoordinate && MathF.Abs(value.Y) <= MaxCoordinate && MathF.Abs(value.Z) <= MaxCoordinate;

    public static bool IsWithin(Vector3 value, float most) => value.Length() <= most;

    public static bool IsFrom0To(float most, float value) => value >= 0f && value <= most;

    public static bool IsTicks(float value) => IsFrom0To(MaxTicks, value) && value == MathF.Floor(value);

    public static string NotAPlace(string what, Vector3 value) => string.Create(
        CultureInfo.InvariantCulture,
        $"{what} cannot be ({value.X}, {value.Y}, {value.Z}): it takes three numbers, each within a billion units of the origin.");

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
