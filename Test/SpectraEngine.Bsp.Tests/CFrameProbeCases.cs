using System.Numerics;
using SpectraEngine.Core.Scene;
using static SpectraEngine.Bsp.Tests.Probe;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The cases Roblox Studio is asked about, each with the engine's own answer.
/// The hand-worked cases of the CFrame tests are here again, and a seeded set
/// of random ones.
/// </summary>
internal sealed class CFrameProbeCases
{
    private const float Quarter = MathF.PI / 2f;
    private const float Half = MathF.PI;
    private const int RandomRounds = 6;

    // Studio's answers are for the numbers this seed gives. Change it and
    // Studio has to be asked again.
    private const uint Seed = 20261011;

    private static readonly Probed<CFrame> A = At(1f, 2f, 3f).Times(Probe.Angles(0f, Quarter, 0f));
    private static readonly Probed<CFrame> B = At(4f, 5f, 6f).Times(Probe.Angles(Quarter, 0f, 0f));

    private readonly List<CFrameProbeCase> _cases = [];

    public static IReadOnlyList<CFrameProbeCase> All { get; } = new CFrameProbeCases().Build();

    private List<CFrameProbeCase> Build()
    {
        AddRotations();
        AddProducts();
        AddSpaces();
        AddLooks();
        AddLerps();
        AddChoices();
        AddRandom();
        return _cases;
    }

    private void Frame(string name, Probed<CFrame> frame, bool ourChoice = false) =>
        _cases.Add(new CFrameProbeCase(name, CFrameProbeCase.Frame, frame.Luau, frame.Value.Components(), ourChoice));

    private void Vector(string name, Probed<Vector3> vector) =>
        _cases.Add(new CFrameProbeCase(name, CFrameProbeCase.Vector, vector.Luau, Numbers(vector.Value), false));

    private void Angles(string name, Probed<Vector3> angles, bool ourChoice = false) =>
        _cases.Add(new CFrameProbeCase(name, CFrameProbeCase.Angles, angles.Luau, Numbers(angles.Value), ourChoice));

    private static float[] Numbers(Vector3 v) => [v.X, v.Y, v.Z];

    private void AddRotations()
    {
        Frame("identity", Identity);
        Vector("identity_look_vector", Identity.LookVector());
        Vector("identity_right_vector", Identity.RightVector());
        Vector("identity_up_vector", Identity.UpVector());

        Frame("angles_quarter_x", Probe.Angles(Quarter, 0f, 0f));
        Frame("angles_quarter_y", Probe.Angles(0f, Quarter, 0f));
        Frame("angles_quarter_z", Probe.Angles(0f, 0f, Quarter));
        Frame("angles_half_x", Probe.Angles(Half, 0f, 0f));
        Frame("angles_half_y", Probe.Angles(0f, Half, 0f));
        Frame("angles_half_z", Probe.Angles(0f, 0f, Half));
        Frame("angles_quarter_x_y", Probe.Angles(Quarter, Quarter, 0f));
        Frame("angles_quarter_x_z", Probe.Angles(Quarter, 0f, Quarter));
        Frame("angles_quarter_x_y_z", Probe.Angles(Quarter, Quarter, Quarter));
        Frame("angles_slanted", Probe.Angles(0.3f, -0.7f, 1.1f));
        Frame("euler_xyz_slanted", EulerXyz(0.3f, -0.7f, 1.1f));
        Frame("euler_yxz_quarter_x_y", EulerYxz(Quarter, Quarter, 0f));
        Frame("euler_yxz_quarter_x_y_z", EulerYxz(Quarter, Quarter, Quarter));
        Frame("euler_yxz_slanted", EulerYxz(0.3f, -0.7f, 1.1f));
        Frame("orientation_slanted", Orientation(0.3f, -0.7f, 1.1f));

        float third = 1f / MathF.Sqrt(3f);
        Frame("axis_angle_quarter_y", AxisAngle(V(0f, 1f, 0f), Quarter));
        Frame("axis_angle_diagonal_third", AxisAngle(V(third, third, third), 2f * MathF.PI / 3f));

        Probed<CFrame> turned = Probe.Angles(Quarter, 0f, Quarter);
        Vector("x_vector", turned.XVector());
        Vector("y_vector", turned.YVector());
        Vector("z_vector", turned.ZVector());
        Vector("look_vector_quarter_y", Probe.Angles(0f, Quarter, 0f).LookVector());

        Angles("to_euler_angles_xyz_slanted", Probe.Angles(0.3f, -0.7f, 1.1f).ToEulerAnglesXYZ());
        Angles("to_orientation_slanted", Orientation(0.3f, -0.7f, 1.1f).ToOrientation());
        Angles("to_orientation_of_angles", Probe.Angles(0.3f, -0.7f, 1.1f).ToOrientation());
    }

    private void AddProducts()
    {
        Frame("position_times_rotation", A);
        Frame("rotation_times_position", Probe.Angles(0f, Quarter, 0f).Times(At(1f, 2f, 3f)));
        Frame("a_times_b", A.Times(B));
        Frame("b_times_a", B.Times(A));
        Vector("a_times_unit_x", A.Times(V(1f, 0f, 0f)));
        Vector("a_times_unit_z", A.Times(V(0f, 0f, 1f)));
        Vector("a_times_point", A.Times(V(4f, 5f, 6f)));
        Frame("a_plus_vector", A.Plus(V(10f, 20f, 30f)));
        Frame("a_minus_vector", A.Minus(V(10f, 20f, 30f)));
        Frame("a_inverse", A.Inverse());
        Frame("a_times_its_inverse", A.Times(A.Inverse()));
    }

    private void AddSpaces()
    {
        Frame("to_world_space", A.ToWorldSpace(B));
        Frame("to_object_space", A.ToObjectSpace(B));
        Vector("point_to_world_space", A.PointToWorldSpace(V(1f, 0f, 0f)));
        Vector("point_to_object_space", A.PointToObjectSpace(V(1f, 2f, 2f)));
        Vector("point_to_object_space_of_origin", A.PointToObjectSpace(V(0f, 0f, 0f)));
        Vector("vector_to_world_space_x", A.VectorToWorldSpace(V(1f, 0f, 0f)));
        Vector("vector_to_world_space_z", A.VectorToWorldSpace(V(0f, 0f, 1f)));
        Vector("vector_to_object_space_x", A.VectorToObjectSpace(V(1f, 0f, 0f)));
        Vector("vector_to_object_space_minus_z", A.VectorToObjectSpace(V(0f, 0f, -1f)));
    }

    private void AddLooks()
    {
        Probed<Vector3> origin = V(0f, 0f, 0f);
        Frame("look_at_minus_z", LookAt(origin, V(0f, 0f, -5f)));
        Frame("look_at_plus_z", LookAt(origin, V(0f, 0f, 5f)));
        Frame("look_at_plus_x", LookAt(origin, V(5f, 0f, 0f)));
        Frame("look_at_minus_x", LookAt(origin, V(-5f, 0f, 0f)));
        Frame("look_at_level", LookAt(V(1f, 2f, 3f), V(4f, 2f, -1f)));
        Frame("look_at_pitched", LookAt(origin, V(0f, 3f, -4f)));
        Frame("look_at_slanted", LookAt(V(1f, 1f, 1f), V(3f, 2f, -1f)));
        Frame("look_at_up_along_x", LookAt(origin, V(0f, 0f, -1f), V(1f, 0f, 0f)));
        Frame("look_at_long_up", LookAt(origin, V(3f, 0f, -4f), V(0f, 9f, 0f)));
        Frame("look_along_slanted", LookAlong(V(1f, 2f, 3f), V(2f, 1f, -2f)));
        Frame("look_along_up_along_x", LookAlong(V(1f, 2f, 3f), V(2f, 1f, -2f), V(1f, 0f, 0f)));

        Frame("from_matrix_three_columns", Matrix(V(1f, 2f, 3f), V(0f, 0f, -1f), V(0f, 1f, 0f), V(1f, 0f, 0f)));
        Frame("from_matrix_two_columns", Matrix(origin, V(0f, 0f, -1f), V(0f, 1f, 0f)));
    }

    private void AddLerps()
    {
        Probed<CFrame> quarterY = At(10f, 0f, -4f).Times(Probe.Angles(0f, Quarter, 0f));
        Frame("lerp_half_way", Identity.Lerp(quarterY, 0.5f));
        Frame("lerp_at_zero", A.Lerp(B, 0f));
        Frame("lerp_at_one", A.Lerp(B, 1f));
        Frame("lerp_a_quarter_of_the_way", Identity.Lerp(Probe.Angles(0f, 0f, 2f), 0.25f));
        Frame("lerp_the_short_way", Identity.Lerp(Probe.Angles(0f, 3f * Quarter, 0f), 0.5f));
    }

    // Roblox's documentation gives no answer for any of these.
    private void AddChoices()
    {
        Probed<Vector3> origin = V(0f, 0f, 0f);
        Frame("choice_look_at_straight_up", LookAt(V(1f, 2f, 3f), V(1f, 9f, 3f)), ourChoice: true);
        Frame("choice_look_at_straight_down", LookAt(origin, V(0f, -4f, 0f)), ourChoice: true);
        Frame("choice_look_at_along_given_up", LookAt(origin, V(5f, 0f, 0f), V(1f, 0f, 0f)), ourChoice: true);
        Frame("choice_look_at_own_position", LookAt(V(1f, 2f, 3f), V(1f, 2f, 3f)), ourChoice: true);
        Frame("choice_look_along_no_direction", LookAlong(V(1f, 2f, 3f), origin), ourChoice: true);
        Frame("choice_axis_angle_no_axis", AxisAngle(origin, 1f), ourChoice: true);

        // The documentation asks for a unit axis. Ours may have any length.
        Frame("choice_axis_angle_long_axis", AxisAngle(V(0f, 7f, 0f), Quarter), ourChoice: true);
        Frame("choice_axis_angle_long_diagonal", AxisAngle(V(1f, 1f, 1f), 2f * MathF.PI / 3f), ourChoice: true);
        Frame("choice_from_matrix_parallel_columns", Matrix(V(1f, 2f, 3f), V(1f, 0f, 0f), V(2f, 0f, 0f)), ourChoice: true);
        Angles("choice_to_euler_angles_xyz_at_pole", Probe.Angles(0.4f, Quarter, -0.9f).ToEulerAnglesXYZ(), ourChoice: true);
        Angles("choice_to_orientation_at_pole", Orientation(Quarter, 0.4f, -0.9f).ToOrientation(), ourChoice: true);
    }

    private void AddRandom()
    {
        var numbers = new ProbeNumbers(Seed);
        for (int round = 0; round < RandomRounds; round++)
        {
            string name = $"random_{round}_";
            (Probed<CFrame> a, Probed<CFrame> b) = FramesWithOneAnswer(numbers);
            Probed<Vector3> v = RandomVector(numbers, 20f);

            Frame(name + "frame", a);
            Frame(name + "product", a.Times(b));
            Frame(name + "inverse", a.Inverse());
            Frame(name + "to_object_space", a.ToObjectSpace(b));
            Frame(name + "lerp", a.Lerp(b, numbers.Next(0.1f, 0.9f)));
            Vector(name + "times_point", a.Times(v));
            Vector(name + "point_to_object_space", a.PointToObjectSpace(v));
            Vector(name + "vector_to_world_space", a.VectorToWorldSpace(v));
            Vector(name + "vector_to_object_space", a.VectorToObjectSpace(v));
            Vector(name + "look_vector", a.LookVector());
            Vector(name + "right_vector", a.RightVector());
            Vector(name + "up_vector", a.UpVector());
            Angles(name + "to_euler_angles_xyz", a.ToEulerAnglesXYZ());
            Angles(name + "to_orientation", a.ToOrientation());

            float rx = numbers.Next(-3f, 3f), ry = numbers.Next(-3f, 3f), rz = numbers.Next(-3f, 3f);
            Frame(name + "angles", Probe.Angles(rx, ry, rz));
            Frame(name + "euler_yxz", EulerYxz(rx, ry, rz));
            Vector3 axis = Vector3.Normalize(Direction(numbers).Value);
            Frame(name + "axis_angle", AxisAngle(V(axis.X, axis.Y, axis.Z), numbers.Next(-3f, 3f)));

            Probed<Vector3> at = RandomVector(numbers, 20f);
            Probed<Vector3> look = Direction(numbers);
            Probed<Vector3> up = Direction(numbers, look.Value);
            Probed<Vector3> target = Target(numbers, at.Value, up.Value);
            Frame(name + "look_along", LookAlong(at, look));
            Frame(name + "look_along_with_up", LookAlong(at, look, up));
            Frame(name + "look_at", LookAt(at, target));
            Frame(name + "look_at_with_up", LookAt(at, target, up));
        }
    }

    // Near an Euler pole, or half a turn apart, more than one answer is right
    // and Studio's need not be ours. Such a draw is passed over.
    private static (Probed<CFrame> A, Probed<CFrame> B) FramesWithOneAnswer(ProbeNumbers numbers)
    {
        while (true)
        {
            Probed<CFrame> a = RandomFrame(numbers);
            Probed<CFrame> b = RandomFrame(numbers);
            Vector3 z = a.Value.ZVector;
            bool nearPole = MathF.Abs(z.X) > 0.95f || MathF.Abs(z.Y) > 0.95f;
            bool halfTurnApart = MathF.Abs(Quaternion.Dot(a.Value.Rotation, b.Value.Rotation)) < 0.2f;
            if (!nearPole && !halfTurnApart)
                return (a, b);
        }
    }

    // A vector with some length that runs along neither the given line nor Y:
    // a look along up has no answer in Roblox's documentation.
    private static Probed<Vector3> Direction(ProbeNumbers numbers, Vector3 notAlong = default)
    {
        while (true)
        {
            Probed<Vector3> direction = RandomVector(numbers, 1f);
            if (direction.Value.Length() > 0.3f && IsClearOf(direction.Value, notAlong))
                return direction;
        }
    }

    // A point to look at from another, for the same reason.
    private static Probed<Vector3> Target(ProbeNumbers numbers, Vector3 from, Vector3 up)
    {
        while (true)
        {
            Probed<Vector3> target = RandomVector(numbers, 20f);
            if (Vector3.Distance(target.Value, from) > 1f && IsClearOf(target.Value - from, up))
                return target;
        }
    }

    private static bool IsClearOf(Vector3 direction, Vector3 line)
    {
        Vector3 unit = Vector3.Normalize(direction);
        return Vector3.Cross(unit, Vector3.UnitY).Length() > 0.2f &&
            (line == Vector3.Zero || Vector3.Cross(unit, Vector3.Normalize(line)).Length() > 0.2f);
    }

    private static Probed<CFrame> RandomFrame(ProbeNumbers numbers) =>
        At(numbers.Next(-20f, 20f), numbers.Next(-20f, 20f), numbers.Next(-20f, 20f))
            .Times(Probe.Angles(numbers.Next(-3f, 3f), numbers.Next(-3f, 3f), numbers.Next(-3f, 3f)));

    private static Probed<Vector3> RandomVector(ProbeNumbers numbers, float reach) =>
        V(numbers.Next(-reach, reach), numbers.Next(-reach, reach), numbers.Next(-reach, reach));
}
