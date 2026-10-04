using System.Numerics;

namespace SpectraEngine.Core.Physics;

/// <summary>
/// The tick rate and the world constants, in spectraunits. One spectraunit
/// (<c>sunit</c>) is one metre.
/// </summary>
public static class PhysicsDefaults
{
    /// <summary>Metres in one spectraunit.</summary>
    public const float MetresPerUnit = 1f;

    /// <summary>Roblox's documented stud, for porting.</summary>
    public const float MetresPerRobloxStud = 0.28f;

    /// <summary>
    /// The stud that Roblox's default gravity (196.2 studs/s²) implies. It
    /// differs from the documented one; pick one per project when porting.
    /// </summary>
    public const float MetresPerRobloxGravityStud = 0.05f;

    /// <summary>Earth gravity, in sunits per second squared.</summary>
    public static readonly Vector3 Gravity = new(0f, -9.81f, 0f);

    /// <summary>Simulation ticks per second, independent of the frame rate.</summary>
    public const int TicksPerSecond = 60;

    /// <summary>Seconds in one tick.</summary>
    public const float FixedDeltaTime = 1f / TicksPerSecond;

    /// <summary>
    /// The most ticks one frame may run before the accumulator drops the
    /// remaining time.
    /// </summary>
    public const int MaxTicksPerFrame = 5;

    /// <summary>
    /// Speed below which a body may be put to sleep, in sunits per second.
    /// </summary>
    public const float SleepThreshold = 0.05f;

    /// <summary>Default body density, in kg per cubic sunit.</summary>
    public const float Density = 1000f;
}
