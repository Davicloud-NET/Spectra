using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Euler angles round-tripped against the stored quaternion.</summary>
// Euler triples are many-to-one, so these compare rotations, not angle values.
public sealed class EulerAnglesTests
{
    private static void ShouldRotateAlike(Quaternion expected, Quaternion actual, string because)
    {
        // Compare rotated probes, not components: q and -q are the same rotation.
        foreach (Vector3 probe in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            Vector3 want = Vector3.Transform(probe, expected);
            Vector3 got = Vector3.Transform(probe, actual);
            Vector3.Distance(want, got).ShouldBeLessThan(1e-4f, because);
        }
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(90f, 0f, 0f)]
    [InlineData(0f, 45f, 0f)]
    [InlineData(0f, 0f, 30f)]
    [InlineData(35f, 20f, -15f)]
    [InlineData(-170f, 12f, 175f)]
    [InlineData(12.5f, -47.25f, 88f)]
    public void A_rotation_survives_a_trip_through_euler_angles(float yaw, float pitch, float roll)
    {
        Quaternion original = new EulerAngles(yaw, pitch, roll).ToQuaternion();

        Quaternion round = EulerAngles.FromQuaternion(original).ToQuaternion();

        ShouldRotateAlike(original, round, $"yaw {yaw}, pitch {pitch}, roll {roll}");
    }

    [Fact]
    public void The_convention_matches_the_one_the_engine_already_writes_in_code()
    {
        var angles = new EulerAngles(Yaw: 25f, Pitch: -40f, Roll: 10f);

        Quaternion expected = Quaternion.CreateFromYawPitchRoll(
            25f * MathF.PI / 180f, -40f * MathF.PI / 180f, 10f * MathF.PI / 180f);

        ShouldRotateAlike(expected, angles.ToQuaternion(), "the yaw/pitch/roll order is the engine's");
    }

    [Theory]
    [InlineData(90f)]
    [InlineData(-90f)]
    public void Gimbal_lock_still_produces_the_rotation_that_was_asked_for(float pitch)
    {
        // At +/-90 only yaw + roll is recoverable, so the angles may come back
        // redistributed. The rotation must not change and nothing may be NaN.
        Quaternion original = new EulerAngles(Yaw: 30f, Pitch: pitch, Roll: 20f).ToQuaternion();

        EulerAngles extracted = EulerAngles.FromQuaternion(original);

        float.IsNaN(extracted.Yaw).ShouldBeFalse();
        float.IsNaN(extracted.Pitch).ShouldBeFalse();
        float.IsNaN(extracted.Roll).ShouldBeFalse();
        ShouldRotateAlike(original, extracted.ToQuaternion(), $"at pitch {pitch}");
    }

    [Fact]
    public void A_drifted_quaternion_is_normalised_rather_than_producing_nonsense()
    {
        // asin is undefined past 1, which a non-unit quaternion can reach.
        Quaternion drifted = new EulerAngles(10f, 20f, 30f).ToQuaternion() * 1.05f;

        EulerAngles extracted = EulerAngles.FromQuaternion(drifted);

        float.IsNaN(extracted.Pitch).ShouldBeFalse();
        ShouldRotateAlike(Quaternion.Normalize(drifted), extracted.ToQuaternion(), "drifted input");
    }

    [Fact]
    public void The_display_vector_is_pitch_yaw_roll_in_x_y_z()
    {
        var angles = new EulerAngles(Yaw: 1f, Pitch: 2f, Roll: 3f);

        angles.AsDegrees.ShouldBe(new Vector3(2f, 1f, 3f));
        EulerAngles.FromDegrees(angles.AsDegrees).ShouldBe(angles);
    }
}
