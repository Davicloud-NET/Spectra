using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// CFrame held against <see cref="RobloxFrame"/>, the rules written out with
/// plain matrices, over seeded random inputs.
/// </summary>
public sealed class CFrameReferenceTests
{
    private const int Rounds = 400;
    private const float Loose = 1e-4f;

    // One random frame built both ways from the same numbers.
    private static (CFrame Ours, RobloxFrame Theirs) Pair(Random random)
    {
        Vector3 position = RandomFrames.Vector(random);
        Vector3 axis = RandomFrames.Axis(random);
        float angle = RandomFrames.Angle(random);

        return (
            new CFrame(position) * CFrame.FromAxisAngle(axis, angle),
            RobloxFrame.At(position) * RobloxFrame.AxisAngle(axis, angle));
    }

    private static void ShouldMatch(CFrame ours, RobloxFrame theirs) =>
        CFrameAssertions.ShouldBeNear(ours.Components(), theirs.Components(), Loose, "The frame");

    [Fact]
    public void Angles_is_the_x_matrix_times_the_y_matrix_times_the_z_matrix()
    {
        var random = new Random(5201);
        for (int i = 0; i < Rounds; i++)
        {
            float rx = RandomFrames.Angle(random), ry = RandomFrames.Angle(random), rz = RandomFrames.Angle(random);

            ShouldMatch(CFrame.Angles(rx, ry, rz), RobloxFrame.Angles(rx, ry, rz));
            ShouldMatch(CFrame.FromEulerAnglesXYZ(rx, ry, rz), RobloxFrame.Angles(rx, ry, rz));
        }
    }

    [Fact]
    public void FromEulerAnglesYXZ_is_the_y_matrix_times_the_x_matrix_times_the_z_matrix()
    {
        var random = new Random(5202);
        for (int i = 0; i < Rounds; i++)
        {
            float rx = RandomFrames.Angle(random), ry = RandomFrames.Angle(random), rz = RandomFrames.Angle(random);

            ShouldMatch(CFrame.FromEulerAnglesYXZ(rx, ry, rz), RobloxFrame.Orientation(rx, ry, rz));
            ShouldMatch(CFrame.FromOrientation(rx, ry, rz), RobloxFrame.Orientation(rx, ry, rz));
        }
    }

    [Fact]
    public void FromAxisAngle_is_the_matrix_of_a_right_handed_turn()
    {
        var random = new Random(5203);
        for (int i = 0; i < Rounds; i++)
        {
            Vector3 axis = RandomFrames.Axis(random);
            float angle = RandomFrames.Angle(random);

            ShouldMatch(CFrame.FromAxisAngle(axis, angle), RobloxFrame.AxisAngle(axis, angle));
        }
    }

    [Fact]
    public void The_product_is_the_matrix_product_with_the_offset_turned_by_the_left_frame()
    {
        var random = new Random(5204);
        for (int i = 0; i < Rounds; i++)
        {
            var a = Pair(random);
            var b = Pair(random);

            ShouldMatch(a.Ours * b.Ours, a.Theirs * b.Theirs);
        }
    }

    [Fact]
    public void A_chain_of_products_matches_the_matrices_multiplied_in_the_same_order()
    {
        var random = new Random(5205);
        for (int i = 0; i < Rounds / 4; i++)
        {
            var chain = Pair(random);
            for (int link = 0; link < 5; link++)
            {
                var next = Pair(random);
                chain = (chain.Ours * next.Ours, chain.Theirs * next.Theirs);
            }

            CFrameAssertions.ShouldBeNear(chain.Ours.Components(), chain.Theirs.Components(), 1e-3f, "The chain");
        }
    }

    [Fact]
    public void The_inverse_is_the_transpose_and_the_position_turned_back()
    {
        var random = new Random(5206);
        for (int i = 0; i < Rounds; i++)
        {
            var a = Pair(random);

            ShouldMatch(a.Ours.Inverse(), a.Theirs.Inverse());
        }
    }

    [Fact]
    public void A_frame_times_a_point_is_the_position_plus_the_matrix_times_the_point()
    {
        var random = new Random(5207);
        for (int i = 0; i < Rounds; i++)
        {
            var a = Pair(random);
            Vector3 point = RandomFrames.Vector(random);

            (a.Ours * point).ShouldBeNear(a.Theirs.Apply(point), Loose);
            a.Ours.PointToObjectSpace(point).ShouldBeNear(a.Theirs.Inverse().Apply(point), Loose);
        }
    }

    [Fact]
    public void ToObjectSpace_is_the_inverse_matrix_times_the_other_frame()
    {
        var random = new Random(5208);
        for (int i = 0; i < Rounds; i++)
        {
            var a = Pair(random);
            var b = Pair(random);

            ShouldMatch(a.Ours.ToObjectSpace(b.Ours), a.Theirs.Inverse() * b.Theirs);
        }
    }

    [Fact]
    public void LookAt_builds_right_up_and_back_the_way_the_rule_says()
    {
        var random = new Random(5209);
        int checkedFrames = 0;
        for (int i = 0; i < Rounds; i++)
        {
            Vector3 at = RandomFrames.Vector(random);
            Vector3 target = RandomFrames.Vector(random);
            Vector3 up = RandomFrames.Axis(random);
            Vector3 look = Vector3.Normalize(target - at);

            // Near a look along up the rule has no answer.
            if (Vector3.Cross(look, Vector3.Normalize(up)).Length() < 0.05f ||
                Vector3.Cross(look, Vector3.UnitY).Length() < 0.05f)
            {
                continue;
            }

            ShouldMatch(CFrame.LookAt(at, target), RobloxFrame.LookAlong(at, target - at, Vector3.UnitY));
            ShouldMatch(CFrame.LookAt(at, target, up), RobloxFrame.LookAlong(at, target - at, up));
            ShouldMatch(CFrame.LookAlong(at, target - at, up), RobloxFrame.LookAlong(at, target - at, up));
            checkedFrames++;
        }

        checkedFrames.ShouldBeGreaterThan(Rounds / 2);
    }
}
