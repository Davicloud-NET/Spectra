using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>The ways a script makes a CFrame.</summary>
[ScriptLibrary("CFrame")]
internal static partial class CFrameLibrary
{
    private const string NewTakes =
        "CFrame.new takes nothing, a position, a position and a point to look at, or 3, 7 or 12 numbers";

    /// <summary>The frame at the origin with no rotation.</summary>
    [ScriptProperty(Name = "identity")]
    public static CFrame Identity => CFrame.Identity;

    /// <summary>
    /// Makes a frame: from nothing, from a position, from a position and a point to look at, or from 3, 7 or
    /// 12 numbers. Seven are a position and a quaternion. Twelve are a position and a rotation matrix row by row.
    /// </summary>
    // Written by hand: which shape it is called in is decided by the count
    // and the kinds of its arguments.
    [ScriptFunction(Name = "new")]
    [ScriptOverload("", "CFrame")]
    [ScriptOverload("position: Vector3", "CFrame")]
    [ScriptOverload("position: Vector3, lookAt: Vector3", "CFrame")]
    [ScriptOverload("x: number, y: number, z: number", "CFrame")]
    [ScriptOverload("x: number, y: number, z: number, qX: number, qY: number, qZ: number, qW: number", "CFrame")]
    [ScriptOverload(
        "x: number, y: number, z: number, r00: number, r01: number, r02: number, r10: number, r11: number, " +
        "r12: number, r20: number, r21: number, r22: number",
        "CFrame")]
    private static int New(LuauStack stack)
    {
        int count = stack.Count;
        if (count == 0)
            return Push(stack, CFrame.Identity);

        if (stack.TryGetVector(1, out Vector3 position))
        {
            if (count == 1)
                return Push(stack, new CFrame(position));

            return stack.TryGetVector(2, out Vector3 target)
                ? Push(stack, CFrame.LookAt(position, target))
                : ScriptArguments.Wrong(stack, 2, "new", "Vector3");
        }

        return count is 3 or 7 or 12 ? FromNumbers(stack, count) : stack.Fail(NewTakes);
    }

    /// <summary>A rotation by angles about X, Y and Z in radians, applied in that order.</summary>
    [ScriptFunction]
    public static CFrame Angles(float rx, float ry, float rz) => CFrame.Angles(rx, ry, rz);

    /// <summary>The same as Angles.</summary>
    [ScriptFunction(Name = "fromEulerAnglesXYZ")]
    public static CFrame FromEulerAnglesXYZ(float rx, float ry, float rz) => CFrame.FromEulerAnglesXYZ(rx, ry, rz);

    /// <summary>A rotation by angles about X, Y and Z in radians, applied in the order Y, X, Z.</summary>
    [ScriptFunction(Name = "fromEulerAnglesYXZ")]
    public static CFrame FromEulerAnglesYXZ(float rx, float ry, float rz) => CFrame.FromEulerAnglesYXZ(rx, ry, rz);

    /// <summary>The same as fromEulerAnglesYXZ.</summary>
    [ScriptFunction(Name = "fromOrientation")]
    public static CFrame FromOrientation(float rx, float ry, float rz) => CFrame.FromOrientation(rx, ry, rz);

    /// <summary>A frame at a place that faces a point. Up is +Y unless it is given.</summary>
    [ScriptFunction(Name = "lookAt")]
    public static CFrame LookAt(Vector3 at, Vector3 lookAt, Vector3? up = null) =>
        CFrame.LookAt(at, lookAt, up ?? Vector3.UnitY);

    /// <summary>A frame at a place that faces along a direction. Up is +Y unless it is given.</summary>
    [ScriptFunction(Name = "lookAlong")]
    public static CFrame LookAlong(Vector3 at, Vector3 direction, Vector3? up = null) =>
        CFrame.LookAlong(at, direction, up ?? Vector3.UnitY);

    /// <summary>A rotation by an angle in radians about an axis.</summary>
    [ScriptFunction(Name = "fromAxisAngle")]
    public static CFrame FromAxisAngle(Vector3 axis, float angle) => CFrame.FromAxisAngle(axis, angle);

    /// <summary>A frame from a position and its X and Y axes. The Z axis follows from the two.</summary>
    // The third column may be handed over and is not read: a rotation's
    // follows from the first two.
    [ScriptFunction(Name = "fromMatrix")]
    public static CFrame FromMatrix(Vector3 position, Vector3 vX, Vector3 vY, Vector3? vZ = null) =>
        CFrame.FromMatrix(position, vX, vY);

    // Three numbers are a position. Four more are a quaternion, x first. Nine
    // more are a rotation matrix row by row, as GetComponents gives it.
    private static int FromNumbers(LuauStack stack, int count)
    {
        Span<float> n = stackalloc float[CFrame.ComponentCount];
        for (int index = 1; index <= count; index++)
        {
            if (!ScriptArguments.TryNumber(stack, index, out n[index - 1]))
                return ScriptArguments.Wrong(stack, index, "new", "number");
        }

        var position = new Vector3(n[0], n[1], n[2]);
        return count switch
        {
            3 => Push(stack, new CFrame(position)),
            7 => Push(stack, new CFrame(position, new Quaternion(n[3], n[4], n[5], n[6]))),
            _ => Push(stack, CFrame.FromMatrix(
                position, new Vector3(n[3], n[6], n[9]), new Vector3(n[4], n[7], n[10]), new Vector3(n[5], n[8], n[11]))),
        };
    }

    private static int Push(LuauStack stack, CFrame frame) => ScriptResult.Of(FrameValue.Push(stack, frame));
}
