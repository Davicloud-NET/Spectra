using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

// Roblox's rules for a CFrame written out the way its documentation states
// them: a position and a 3 by 3 matrix that acts on column vectors, in
// doubles. The tests hold the engine's CFrame against it. It uses no
// quaternion and no System.Numerics matrix, so none of the engine's
// conventions can leak into the expected values.
internal sealed class RobloxFrame
{
    private readonly double[] _p;

    // Row by row: R00, R01, R02, R10 and on.
    private readonly double[] _r;

    private RobloxFrame(double[] position, double[] rotation)
    {
        _p = position;
        _r = rotation;
    }

    public static RobloxFrame At(Vector3 position) =>
        new([position.X, position.Y, position.Z], [1, 0, 0, 0, 1, 0, 0, 0, 1]);

    public static RobloxFrame RotationX(double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new([0, 0, 0], [1, 0, 0, 0, c, -s, 0, s, c]);
    }

    public static RobloxFrame RotationY(double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new([0, 0, 0], [c, 0, s, 0, 1, 0, -s, 0, c]);
    }

    public static RobloxFrame RotationZ(double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new([0, 0, 0], [c, -s, 0, s, c, 0, 0, 0, 1]);
    }

    // CFrame.Angles and fromEulerAnglesXYZ: Z is applied first.
    public static RobloxFrame Angles(double rx, double ry, double rz) =>
        RotationX(rx) * RotationY(ry) * RotationZ(rz);

    // fromEulerAnglesYXZ and fromOrientation: Z first, then X, then Y.
    public static RobloxFrame Orientation(double rx, double ry, double rz) =>
        RotationY(ry) * RotationX(rx) * RotationZ(rz);

    // Rodrigues' formula for a right-handed turn about an axis.
    public static RobloxFrame AxisAngle(Vector3 axis, double angle)
    {
        double length = Math.Sqrt((double)axis.X * axis.X + (double)axis.Y * axis.Y + (double)axis.Z * axis.Z);
        double x = axis.X / length, y = axis.Y / length, z = axis.Z / length;
        double c = Math.Cos(angle), s = Math.Sin(angle), t = 1 - c;
        return new([0, 0, 0],
        [
            c + t * x * x, t * x * y - s * z, t * x * z + s * y,
            t * y * x + s * z, c + t * y * y, t * y * z - s * x,
            t * z * x - s * y, t * z * y + s * x, c + t * z * z,
        ]);
    }

    // lookAlong: right is look cross up, up is right cross look, and Z is minus look.
    public static RobloxFrame LookAlong(Vector3 at, Vector3 direction, Vector3 up)
    {
        double[] look = Normalized([direction.X, direction.Y, direction.Z]);
        double[] right = Normalized(Cross(look, [up.X, up.Y, up.Z]));
        double[] upward = Cross(right, look);
        return new([at.X, at.Y, at.Z],
        [
            right[0], upward[0], -look[0],
            right[1], upward[1], -look[1],
            right[2], upward[2], -look[2],
        ]);
    }

    public static RobloxFrame operator *(RobloxFrame a, RobloxFrame b)
    {
        var r = new double[9];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                r[row * 3 + column] =
                    a._r[row * 3] * b._r[column] +
                    a._r[row * 3 + 1] * b._r[3 + column] +
                    a._r[row * 3 + 2] * b._r[6 + column];
            }
        }

        double[] turned = a.Turn(b._p);
        return new([a._p[0] + turned[0], a._p[1] + turned[1], a._p[2] + turned[2]], r);
    }

    // The rotation transposed, and minus that times the position.
    public RobloxFrame Inverse()
    {
        double[] t = [_r[0], _r[3], _r[6], _r[1], _r[4], _r[7], _r[2], _r[5], _r[8]];
        var back = new RobloxFrame([0, 0, 0], t);
        double[] p = back.Turn(_p);
        return new([-p[0], -p[1], -p[2]], t);
    }

    // The frame times a point: the position plus the rotation times the point.
    public Vector3 Apply(Vector3 point)
    {
        double[] turned = Turn([point.X, point.Y, point.Z]);
        return new Vector3((float)(_p[0] + turned[0]), (float)(_p[1] + turned[1]), (float)(_p[2] + turned[2]));
    }

    // What GetComponents returns: the position, then the matrix row by row.
    public float[] Components()
    {
        var components = new float[12];
        for (int i = 0; i < 3; i++)
            components[i] = (float)_p[i];
        for (int i = 0; i < 9; i++)
            components[3 + i] = (float)_r[i];
        return components;
    }

    private double[] Turn(double[] v) =>
    [
        _r[0] * v[0] + _r[1] * v[1] + _r[2] * v[2],
        _r[3] * v[0] + _r[4] * v[1] + _r[5] * v[2],
        _r[6] * v[0] + _r[7] * v[1] + _r[8] * v[2],
    ];

    private static double[] Cross(double[] a, double[] b) =>
    [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    ];

    private static double[] Normalized(double[] v)
    {
        double length = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        return [v[0] / length, v[1] / length, v[2] / length];
    }
}
