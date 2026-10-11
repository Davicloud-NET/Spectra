using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// The CFrame operations, each as a pair: the engine's value and the Luau
// expression that asks Roblox for the same thing with the same numbers.
internal static class Probe
{
    public static Probed<CFrame> Identity => new("CFrame.new()", CFrame.Identity);

    // A float as Luau reads it back to the same float.
    public static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static Probed<Vector3> V(float x, float y, float z) =>
        new($"Vector3.new({Number(x)}, {Number(y)}, {Number(z)})", new Vector3(x, y, z));

    public static Probed<CFrame> At(float x, float y, float z) =>
        new($"CFrame.new({Number(x)}, {Number(y)}, {Number(z)})", new CFrame(x, y, z));

    // CFrame.new with a position and a quaternion, given as qX, qY, qZ, qW.
    public static Probed<CFrame> FromQuaternion(Vector3 position, Quaternion rotation) => new(
        $"CFrame.new({Number(position.X)}, {Number(position.Y)}, {Number(position.Z)}, " +
        $"{Number(rotation.X)}, {Number(rotation.Y)}, {Number(rotation.Z)}, {Number(rotation.W)})",
        new CFrame(position, rotation));

    // CFrame.new with all twelve components, in the order GetComponents gives them.
    public static Probed<CFrame> FromComponents(params float[] c) => new(
        $"CFrame.new({string.Join(", ", c.Select(Number))})",
        CFrame.FromMatrix(
            new Vector3(c[0], c[1], c[2]),
            new Vector3(c[3], c[6], c[9]),
            new Vector3(c[4], c[7], c[10]),
            new Vector3(c[5], c[8], c[11])));

    public static Probed<CFrame> Angles(float rx, float ry, float rz) =>
        new($"CFrame.Angles({Number(rx)}, {Number(ry)}, {Number(rz)})", CFrame.Angles(rx, ry, rz));

    public static Probed<CFrame> EulerXyz(float rx, float ry, float rz) => new(
        $"CFrame.fromEulerAnglesXYZ({Number(rx)}, {Number(ry)}, {Number(rz)})",
        CFrame.FromEulerAnglesXYZ(rx, ry, rz));

    public static Probed<CFrame> EulerYxz(float rx, float ry, float rz) => new(
        $"CFrame.fromEulerAnglesYXZ({Number(rx)}, {Number(ry)}, {Number(rz)})",
        CFrame.FromEulerAnglesYXZ(rx, ry, rz));

    public static Probed<CFrame> Orientation(float rx, float ry, float rz) => new(
        $"CFrame.fromOrientation({Number(rx)}, {Number(ry)}, {Number(rz)})",
        CFrame.FromOrientation(rx, ry, rz));

    public static Probed<CFrame> AxisAngle(Probed<Vector3> axis, float angle) => new(
        $"CFrame.fromAxisAngle({axis.Luau}, {Number(angle)})",
        CFrame.FromAxisAngle(axis.Value, angle));

    public static Probed<CFrame> LookAt(Probed<Vector3> at, Probed<Vector3> target) =>
        new($"CFrame.lookAt({at.Luau}, {target.Luau})", CFrame.LookAt(at.Value, target.Value));

    public static Probed<CFrame> LookAt(Probed<Vector3> at, Probed<Vector3> target, Probed<Vector3> up) => new(
        $"CFrame.lookAt({at.Luau}, {target.Luau}, {up.Luau})",
        CFrame.LookAt(at.Value, target.Value, up.Value));

    public static Probed<CFrame> LookAlong(Probed<Vector3> at, Probed<Vector3> direction) =>
        new($"CFrame.lookAlong({at.Luau}, {direction.Luau})", CFrame.LookAlong(at.Value, direction.Value));

    public static Probed<CFrame> LookAlong(Probed<Vector3> at, Probed<Vector3> direction, Probed<Vector3> up) => new(
        $"CFrame.lookAlong({at.Luau}, {direction.Luau}, {up.Luau})",
        CFrame.LookAlong(at.Value, direction.Value, up.Value));

    public static Probed<CFrame> Matrix(Probed<Vector3> position, Probed<Vector3> vX, Probed<Vector3> vY) => new(
        $"CFrame.fromMatrix({position.Luau}, {vX.Luau}, {vY.Luau})",
        CFrame.FromMatrix(position.Value, vX.Value, vY.Value));

    public static Probed<CFrame> Matrix(
        Probed<Vector3> position, Probed<Vector3> vX, Probed<Vector3> vY, Probed<Vector3> vZ) => new(
        $"CFrame.fromMatrix({position.Luau}, {vX.Luau}, {vY.Luau}, {vZ.Luau})",
        CFrame.FromMatrix(position.Value, vX.Value, vY.Value, vZ.Value));

    public static Probed<CFrame> Times(this Probed<CFrame> a, Probed<CFrame> b) =>
        new($"({a.Luau}) * ({b.Luau})", a.Value * b.Value);

    public static Probed<Vector3> Times(this Probed<CFrame> a, Probed<Vector3> point) =>
        new($"({a.Luau}) * {point.Luau}", a.Value * point.Value);

    public static Probed<CFrame> Plus(this Probed<CFrame> a, Probed<Vector3> offset) =>
        new($"({a.Luau}) + {offset.Luau}", a.Value + offset.Value);

    public static Probed<CFrame> Minus(this Probed<CFrame> a, Probed<Vector3> offset) =>
        new($"({a.Luau}) - {offset.Luau}", a.Value - offset.Value);

    public static Probed<CFrame> Inverse(this Probed<CFrame> a) =>
        new($"({a.Luau}):Inverse()", a.Value.Inverse());

    public static Probed<CFrame> Lerp(this Probed<CFrame> a, Probed<CFrame> goal, float alpha) =>
        new($"({a.Luau}):Lerp({goal.Luau}, {Number(alpha)})", a.Value.Lerp(goal.Value, alpha));

    public static Probed<CFrame> ToWorldSpace(this Probed<CFrame> a, Probed<CFrame> b) =>
        new($"({a.Luau}):ToWorldSpace({b.Luau})", a.Value.ToWorldSpace(b.Value));

    public static Probed<CFrame> ToObjectSpace(this Probed<CFrame> a, Probed<CFrame> b) =>
        new($"({a.Luau}):ToObjectSpace({b.Luau})", a.Value.ToObjectSpace(b.Value));

    public static Probed<Vector3> PointToWorldSpace(this Probed<CFrame> a, Probed<Vector3> point) =>
        new($"({a.Luau}):PointToWorldSpace({point.Luau})", a.Value.PointToWorldSpace(point.Value));

    public static Probed<Vector3> PointToObjectSpace(this Probed<CFrame> a, Probed<Vector3> point) =>
        new($"({a.Luau}):PointToObjectSpace({point.Luau})", a.Value.PointToObjectSpace(point.Value));

    public static Probed<Vector3> VectorToWorldSpace(this Probed<CFrame> a, Probed<Vector3> vector) =>
        new($"({a.Luau}):VectorToWorldSpace({vector.Luau})", a.Value.VectorToWorldSpace(vector.Value));

    public static Probed<Vector3> VectorToObjectSpace(this Probed<CFrame> a, Probed<Vector3> vector) =>
        new($"({a.Luau}):VectorToObjectSpace({vector.Luau})", a.Value.VectorToObjectSpace(vector.Value));

    public static Probed<Vector3> LookVector(this Probed<CFrame> a) => new($"({a.Luau}).LookVector", a.Value.LookVector);

    public static Probed<Vector3> RightVector(this Probed<CFrame> a) => new($"({a.Luau}).RightVector", a.Value.RightVector);

    public static Probed<Vector3> UpVector(this Probed<CFrame> a) => new($"({a.Luau}).UpVector", a.Value.UpVector);

    public static Probed<Vector3> XVector(this Probed<CFrame> a) => new($"({a.Luau}).XVector", a.Value.XVector);

    public static Probed<Vector3> YVector(this Probed<CFrame> a) => new($"({a.Luau}).YVector", a.Value.YVector);

    public static Probed<Vector3> ZVector(this Probed<CFrame> a) => new($"({a.Luau}).ZVector", a.Value.ZVector);

    // Three angles. In Luau the call returns three numbers, not a vector.
    public static Probed<Vector3> ToEulerAnglesXYZ(this Probed<CFrame> a) =>
        new($"({a.Luau}):ToEulerAnglesXYZ()", a.Value.ToEulerAnglesXYZ());

    // Three angles. In Luau the call returns three numbers, not a vector.
    public static Probed<Vector3> ToOrientation(this Probed<CFrame> a) =>
        new($"({a.Luau}):ToOrientation()", a.Value.ToOrientation());
}
