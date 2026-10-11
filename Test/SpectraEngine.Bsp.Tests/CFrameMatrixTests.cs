using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Where a CFrame meets the engine's matrices, and the System.Numerics facts
/// the type is built on. Each fact is a test, so none rests on memory.
/// </summary>
public sealed class CFrameMatrixTests
{
    private static readonly Quaternion First = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.9f);
    private static readonly Quaternion Second = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.6f);
    private static readonly Vector3 Probe = new(1f, 2f, 3f);

    [Fact]
    public void A_positive_angle_about_y_turns_x_towards_minus_z()
    {
        Quaternion quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        Vector3.Transform(Vector3.UnitX, quarter).ShouldBeNear(new Vector3(0f, 0f, -1f));
    }

    [Fact]
    public void A_quaternion_product_applies_its_right_hand_side_first()
    {
        Vector3 secondThenFirst = Vector3.Transform(Vector3.Transform(Probe, Second), First);

        Vector3.Transform(Probe, First * Second).ShouldBeNear(secondThenFirst);
    }

    [Fact]
    public void Concatenate_applies_its_first_argument_first()
    {
        Vector3 firstThenSecond = Vector3.Transform(Vector3.Transform(Probe, First), Second);

        Vector3.Transform(Probe, Quaternion.Concatenate(First, Second)).ShouldBeNear(firstThenSecond);
        (Quaternion.Concatenate(First, Second) - (Second * First)).Length().ShouldBeLessThan(1e-6f);
    }

    [Fact]
    public void A_matrix_from_a_quaternion_holds_the_turned_axes_in_its_rows()
    {
        Quaternion q = First * Second;

        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(q);

        new Vector3(m.M11, m.M12, m.M13).ShouldBeNear(Vector3.Transform(Vector3.UnitX, q));
        new Vector3(m.M21, m.M22, m.M23).ShouldBeNear(Vector3.Transform(Vector3.UnitY, q));
        new Vector3(m.M31, m.M32, m.M33).ShouldBeNear(Vector3.Transform(Vector3.UnitZ, q));
    }

    [Fact]
    public void The_frame_product_is_the_quaternion_product_in_the_same_order()
    {
        var a = new CFrame(Vector3.Zero, First);
        var b = new CFrame(Vector3.Zero, Second);

        (a * b).ShouldBeNear(new CFrame(Vector3.Zero, First * Second));
        (a * b).ShouldBeNear(new CFrame(Vector3.Zero, Quaternion.Concatenate(Second, First)));
    }

    [Fact]
    public void ToMatrix_is_the_model_matrix_of_the_same_transform_without_scale()
    {
        CFrame frame = new CFrame(1f, 2f, 3f) * CFrame.Angles(0.3f, -0.7f, 1.1f);
        var transform = new Transform { Position = frame.Position, Rotation = frame.Rotation };

        frame.ToMatrix().ShouldBe(transform.Model);
    }

    [Fact]
    public void ToMatrix_moves_a_point_the_way_the_frame_does()
    {
        CFrame frame = new CFrame(1f, 2f, 3f) * CFrame.Angles(0.3f, -0.7f, 1.1f);

        Vector3.Transform(Probe, frame.ToMatrix()).ShouldBeNear(frame * Probe);
    }

    [Fact]
    public void A_product_of_frames_is_the_matrix_product_the_other_way_round()
    {
        // The engine composes row vectors: world = local * parent.
        CFrame parent = new CFrame(1f, 2f, 3f) * CFrame.Angles(0.3f, -0.7f, 1.1f);
        CFrame local = new CFrame(-2f, 0.5f, 4f) * CFrame.Angles(-1.2f, 0.4f, 2.5f);

        Matrix4x4 world = local.ToMatrix() * parent.ToMatrix();

        CFrame.TryFromMatrix(world, out CFrame fromMatrix).ShouldBeTrue();
        fromMatrix.ShouldBeNear(parent * local);
    }

    [Fact]
    public void TryFromMatrix_reads_back_what_ToMatrix_wrote()
    {
        CFrame frame = new CFrame(1f, 2f, 3f) * CFrame.Angles(0.3f, -0.7f, 1.1f);

        CFrame.TryFromMatrix(frame.ToMatrix(), out CFrame back).ShouldBeTrue();

        back.ShouldBeNear(frame);
    }

    [Fact]
    public void TryFromMatrix_refuses_scale()
    {
        Matrix4x4 scaled = Matrix4x4.CreateScale(2f) * Matrix4x4.CreateRotationY(0.5f);

        CFrame.TryFromMatrix(scaled, out CFrame frame).ShouldBeFalse();
        frame.ShouldBe(CFrame.Identity);
    }

    [Fact]
    public void TryFromMatrix_refuses_scale_along_one_axis()
    {
        Matrix4x4 stretched = Matrix4x4.CreateScale(1f, 1.01f, 1f) * Matrix4x4.CreateRotationY(0.5f);

        CFrame.TryFromMatrix(stretched, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryFromMatrix_refuses_shear()
    {
        Matrix4x4 sheared = Matrix4x4.Identity;
        sheared.M21 = 0.2f;

        CFrame.TryFromMatrix(sheared, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryFromMatrix_refuses_a_mirror()
    {
        CFrame.TryFromMatrix(Matrix4x4.CreateScale(-1f, 1f, 1f), out _).ShouldBeFalse();
    }

    [Fact]
    public void TryFromMatrix_refuses_a_matrix_with_a_NaN()
    {
        Matrix4x4 broken = Matrix4x4.Identity;
        broken.M42 = float.NaN;

        CFrame.TryFromMatrix(broken, out _).ShouldBeFalse();
    }

    [Fact]
    public void Orthonormalize_keeps_the_frame()
    {
        CFrame frame = new CFrame(1f, 2f, 3f) * CFrame.Angles(0.3f, -0.7f, 1.1f);

        CFrame squared = frame.Orthonormalize();

        squared.ShouldBeNear(frame, 1e-6f);
        squared.Rotation.Length().ShouldBe(1f, 1e-6f);
    }
}
