using System;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// A position and a rotation with no scale. Every operation means what it
/// means on a Roblox CFrame: <c>a * b</c> places <c>b</c> in the space of
/// <c>a</c>, and a frame looks along its own -Z with +X right and +Y up.
/// </summary>
// Each operation is written as what it does to a point. The engine's matrices
// act on row vectors and Roblox's on column vectors, so a matrix product
// copied from either side would come out in the wrong order.
public readonly struct CFrame : IEquatable<CFrame>
{
    /// <summary>How many numbers <see cref="GetComponents"/> writes.</summary>
    public const int ComponentCount = 12;

    // Rounding alone leaves a product of unit quaternions this far from unit
    // length. A rotation within it is kept as it is, so multiplying by the
    // identity gives back the same rotation. One past it is renormalized,
    // which is what stops drift in a long chain of products.
    private const float UnitSlack = 1e-6f;

    // All zero in a default frame, which reads as no rotation.
    private readonly Quaternion _rotation;

    /// <summary>A frame at a position with no rotation.</summary>
    public CFrame(Vector3 position)
        : this(position, Quaternion.Identity)
    {
    }

    /// <summary>A frame at a position with no rotation.</summary>
    public CFrame(float x, float y, float z)
        : this(new Vector3(x, y, z), Quaternion.Identity)
    {
    }

    /// <summary>
    /// A frame from a position and a rotation. A rotation of unit length is
    /// kept bit for bit, any other is brought to unit length, and one of no
    /// length is no rotation.
    /// </summary>
    public CFrame(Vector3 position, Quaternion rotation)
    {
        Position = position;
        _rotation = Unit(rotation);
    }

    /// <summary>The frame at the origin with no rotation.</summary>
    public static CFrame Identity => new(Vector3.Zero, Quaternion.Identity);

    /// <summary>Where the frame is.</summary>
    public Vector3 Position { get; }

    /// <summary>The rotation, always of unit length.</summary>
    public Quaternion Rotation => _rotation == default ? Quaternion.Identity : _rotation;

    /// <summary>Where the frame's +X points. The first column of the rotation matrix.</summary>
    public Vector3 XVector
    {
        get
        {
            Matrix4x4 basis = Basis;
            return new Vector3(basis.M11, basis.M12, basis.M13);
        }
    }

    /// <summary>Where the frame's +Y points. The second column of the rotation matrix.</summary>
    public Vector3 YVector
    {
        get
        {
            Matrix4x4 basis = Basis;
            return new Vector3(basis.M21, basis.M22, basis.M23);
        }
    }

    /// <summary>Where the frame's +Z points. The third column of the rotation matrix.</summary>
    public Vector3 ZVector
    {
        get
        {
            Matrix4x4 basis = Basis;
            return new Vector3(basis.M31, basis.M32, basis.M33);
        }
    }

    /// <summary>The way the frame faces: minus <see cref="ZVector"/>.</summary>
    public Vector3 LookVector => -ZVector;

    /// <summary>The frame's right: <see cref="XVector"/>.</summary>
    public Vector3 RightVector => XVector;

    /// <summary>The frame's up: <see cref="YVector"/>.</summary>
    public Vector3 UpVector => YVector;

    // System.Numerics builds matrices for row vectors, so the rows of this one
    // are XVector, YVector and ZVector. Equality and GetComponents read the
    // same matrix, so equal frames have equal components.
    private Matrix4x4 Basis => Matrix4x4.CreateFromQuaternion(Rotation);

    /// <summary>Rotation by radians about X, Y and Z, with the Z turn applied first.</summary>
    public static CFrame Angles(float rx, float ry, float rz) => FromEulerAnglesXYZ(rx, ry, rz);

    /// <summary>Rotation by radians about X, Y and Z, with the Z turn applied first.</summary>
    public static CFrame FromEulerAnglesXYZ(float rx, float ry, float rz) =>
        new(Vector3.Zero, EulerRotation.FromXyz(rx, ry, rz));

    /// <summary>Rotation by radians with the Z turn applied first, then X, then Y.</summary>
    public static CFrame FromEulerAnglesYXZ(float rx, float ry, float rz) =>
        new(Vector3.Zero, EulerRotation.FromYxz(rx, ry, rz));

    /// <summary>The same as <see cref="FromEulerAnglesYXZ"/>.</summary>
    public static CFrame FromOrientation(float rx, float ry, float rz) => FromEulerAnglesYXZ(rx, ry, rz);

    /// <summary>
    /// A right-handed rotation by radians about an axis of any length. An axis
    /// of no length gives no rotation.
    /// </summary>
    public static CFrame FromAxisAngle(Vector3 axis, float angle) =>
        UnitLength.TryOf(axis, out Vector3 unit)
            ? new CFrame(Vector3.Zero, Quaternion.CreateFromAxisAngle(unit, angle))
            : Identity;

    /// <summary>A frame at one point that looks at another, with +Y up.</summary>
    public static CFrame LookAt(Vector3 at, Vector3 target) => LookAlong(at, target - at, Vector3.UnitY);

    /// <summary>A frame at one point that looks at another.</summary>
    public static CFrame LookAt(Vector3 at, Vector3 target, Vector3 up) => LookAlong(at, target - at, up);

    /// <summary>A frame at a point that looks along a direction, with +Y up.</summary>
    public static CFrame LookAlong(Vector3 at, Vector3 direction) => LookAlong(at, direction, Vector3.UnitY);

    /// <summary>
    /// A frame at a point that looks along a direction. A direction along
    /// <paramref name="up"/> is treated as if up were +Y, and one straight up
    /// or down keeps the right vector on +X. No direction gives no rotation.
    /// </summary>
    public static CFrame LookAlong(Vector3 at, Vector3 direction, Vector3 up) =>
        new(at, LookRotation.Along(direction, up));

    /// <summary>
    /// A frame from a position and the first two columns of a rotation matrix.
    /// The third is their cross product.
    /// </summary>
    public static CFrame FromMatrix(Vector3 position, Vector3 vX, Vector3 vY) =>
        new(position, LookRotation.FromAxes(vX, vY));

    /// <summary>
    /// A frame from a position and the columns of a rotation matrix. A frame
    /// here holds no scale, shear or mirror: <paramref name="vX"/> keeps its
    /// direction and <paramref name="vY"/> is squared up against it.
    /// </summary>
    /// <param name="vZ">Not read. A rotation's third column follows from the first two.</param>
    public static CFrame FromMatrix(Vector3 position, Vector3 vX, Vector3 vY, Vector3 vZ) =>
        FromMatrix(position, vX, vY);

    /// <summary>
    /// Reads a frame from an engine matrix. Fails on scale, shear, a mirror or
    /// a value that is not finite.
    /// </summary>
    public static bool TryFromMatrix(Matrix4x4 matrix, out CFrame frame)
    {
        if (Scene.DescribeNonRigidDefect(matrix) is not null)
        {
            frame = Identity;
            return false;
        }

        frame = new CFrame(matrix.Translation, Quaternion.CreateFromRotationMatrix(matrix));
        return true;
    }

    /// <summary>
    /// The frame <paramref name="b"/> names when it is read in the space of
    /// <paramref name="a"/>. Applied to a point it applies b first, then a.
    /// </summary>
    // System.Numerics multiplies quaternions the same way: the right-hand
    // rotation acts first. Quaternion.Concatenate takes them in the other order.
    public static CFrame operator *(CFrame a, CFrame b) =>
        new(a * b.Position, a.Rotation * b.Rotation);

    /// <summary>The point moved from the frame's space into the world.</summary>
    public static Vector3 operator *(CFrame a, Vector3 point) => a.Position + a.Turn(point);

    /// <summary>The frame moved by an offset in world space.</summary>
    public static CFrame operator +(CFrame a, Vector3 offset) => new(a.Position + offset, a.Rotation);

    /// <summary>The frame moved back by an offset in world space.</summary>
    public static CFrame operator -(CFrame a, Vector3 offset) => new(a.Position - offset, a.Rotation);

    public static bool operator ==(CFrame a, CFrame b) => a.Equals(b);

    public static bool operator !=(CFrame a, CFrame b) => !a.Equals(b);

    /// <summary>The frame that undoes this one.</summary>
    public CFrame Inverse() => new(-TurnBack(Position), Quaternion.Conjugate(Rotation));

    /// <summary>
    /// The frame part of the way to <paramref name="goal"/>. The position
    /// moves in a straight line and the rotation along the shortest arc.
    /// </summary>
    public CFrame Lerp(CFrame goal, float alpha) => new(
        Vector3.Lerp(Position, goal.Position, alpha),
        Quaternion.Slerp(Rotation, goal.Rotation, alpha));

    /// <summary>A frame given in this frame's space, in world space.</summary>
    public CFrame ToWorldSpace(CFrame local) => this * local;

    /// <summary>A frame given in world space, in this frame's space.</summary>
    public CFrame ToObjectSpace(CFrame world) => Inverse() * world;

    /// <summary>A point given in this frame's space, in world space.</summary>
    public Vector3 PointToWorldSpace(Vector3 point) => this * point;

    /// <summary>A point given in world space, in this frame's space.</summary>
    public Vector3 PointToObjectSpace(Vector3 point) => TurnBack(point - Position);

    /// <summary>A direction turned from this frame's space into the world. The position plays no part.</summary>
    public Vector3 VectorToWorldSpace(Vector3 vector) => Turn(vector);

    /// <summary>A direction turned from the world into this frame's space. The position plays no part.</summary>
    public Vector3 VectorToObjectSpace(Vector3 vector) => TurnBack(vector);

    /// <summary>
    /// The angles in radians that <see cref="Angles"/> builds this rotation
    /// from, as (rx, ry, rz). With ry at a quarter turn rz is zero.
    /// </summary>
    public Vector3 ToEulerAnglesXYZ() => EulerRotation.ToXyz(Basis);

    /// <summary>
    /// The angles in radians that <see cref="FromEulerAnglesYXZ"/> builds this
    /// rotation from, as (rx, ry, rz). With rx at a quarter turn rz is zero.
    /// </summary>
    public Vector3 ToEulerAnglesYXZ() => EulerRotation.ToYxz(Basis);

    /// <summary>The same as <see cref="ToEulerAnglesYXZ"/>.</summary>
    public Vector3 ToOrientation() => ToEulerAnglesYXZ();

    /// <summary>
    /// Writes the position, then the rotation matrix row by row: x, y, z,
    /// R00, R01, R02, R10, R11, R12, R20, R21, R22.
    /// </summary>
    public void GetComponents(Span<float> components)
    {
        if (components.Length < ComponentCount)
            throw new ArgumentException($"Needs room for {ComponentCount} numbers.", nameof(components));

        Matrix4x4 basis = Basis;
        components[0] = Position.X;
        components[1] = Position.Y;
        components[2] = Position.Z;
        components[3] = basis.M11;
        components[4] = basis.M21;
        components[5] = basis.M31;
        components[6] = basis.M12;
        components[7] = basis.M22;
        components[8] = basis.M32;
        components[9] = basis.M13;
        components[10] = basis.M23;
        components[11] = basis.M33;
    }

    /// <summary>The engine's matrix for this frame, for row vectors like every matrix in the engine.</summary>
    public Matrix4x4 ToMatrix()
    {
        Matrix4x4 matrix = Basis;
        matrix.Translation = Position;
        return matrix;
    }

    /// <summary>The same frame with its rotation brought back to unit length.</summary>
    public CFrame Orthonormalize() => new(Position, Quaternion.Normalize(Rotation));

    /// <summary>True when each of the twelve components is within a tolerance of the other frame's.</summary>
    public bool FuzzyEq(CFrame other, float epsilon = 1e-5f)
    {
        Span<float> mine = stackalloc float[ComponentCount];
        Span<float> theirs = stackalloc float[ComponentCount];
        GetComponents(mine);
        other.GetComponents(theirs);

        for (int i = 0; i < ComponentCount; i++)
        {
            // Written this way round so a NaN is never close to anything.
            if (!(MathF.Abs(mine[i] - theirs[i]) <= epsilon))
                return false;
        }

        return true;
    }

    /// <summary>True when all twelve components are equal.</summary>
    // By the matrix and not the quaternion: q and -q are one rotation.
    public bool Equals(CFrame other) => Position == other.Position && Basis == other.Basis;

    public override bool Equals(object? obj) => obj is CFrame other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Position, Basis);

    /// <summary>The twelve components in the order of <see cref="GetComponents"/>.</summary>
    public override string ToString()
    {
        Span<float> components = stackalloc float[ComponentCount];
        GetComponents(components);

        var parts = new string[ComponentCount];
        for (int i = 0; i < ComponentCount; i++)
            parts[i] = components[i].ToString(CultureInfo.InvariantCulture);
        return string.Join(", ", parts);
    }

    // By the matrix, the way the rule is written. Vector3.Transform with the
    // quaternion leaves the zero vector a rounding error away from zero, and
    // a product of two rotations would then drift off the origin.
    private Vector3 Turn(Vector3 v)
    {
        Matrix4x4 basis = Basis;
        return new Vector3(
            (basis.M11 * v.X) + (basis.M21 * v.Y) + (basis.M31 * v.Z),
            (basis.M12 * v.X) + (basis.M22 * v.Y) + (basis.M32 * v.Z),
            (basis.M13 * v.X) + (basis.M23 * v.Y) + (basis.M33 * v.Z));
    }

    // The matrix transposed, which turns the other way.
    private Vector3 TurnBack(Vector3 v)
    {
        Matrix4x4 basis = Basis;
        return new Vector3(
            (basis.M11 * v.X) + (basis.M12 * v.Y) + (basis.M13 * v.Z),
            (basis.M21 * v.X) + (basis.M22 * v.Y) + (basis.M23 * v.Z),
            (basis.M31 * v.X) + (basis.M32 * v.Y) + (basis.M33 * v.Z));
    }

    private static Quaternion Unit(Quaternion rotation)
    {
        if (MathF.Abs(rotation.LengthSquared() - 1f) <= UnitSlack)
            return rotation;

        // No length, or a value that is not finite, names no rotation.
        return UnitLength.TryOf(rotation, out Quaternion unit) ? unit : Quaternion.Identity;
    }
}
