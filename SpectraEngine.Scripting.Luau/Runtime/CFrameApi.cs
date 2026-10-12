using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>A position and a rotation. A CFrame never changes: every method gives a new one.</summary>
// Each member is a thin call of the Core type's own.
[ScriptValue("CFrame")]
internal static partial class CFrameApi
{
    /// <summary>The position.</summary>
    [ScriptProperty]
    public static Vector3 Position(CFrame self) => self.Position;

    /// <summary>The same rotation at the origin.</summary>
    [ScriptProperty]
    public static CFrame Rotation(CFrame self) => new(Vector3.Zero, self.Rotation);

    /// <summary>The X of the position.</summary>
    [ScriptProperty]
    public static float X(CFrame self) => self.Position.X;

    /// <summary>The Y of the position.</summary>
    [ScriptProperty]
    public static float Y(CFrame self) => self.Position.Y;

    /// <summary>The Z of the position.</summary>
    [ScriptProperty]
    public static float Z(CFrame self) => self.Position.Z;

    /// <summary>The direction the frame faces: its -Z.</summary>
    [ScriptProperty]
    public static Vector3 LookVector(CFrame self) => self.LookVector;

    /// <summary>The direction to the frame's right: its +X.</summary>
    [ScriptProperty]
    public static Vector3 RightVector(CFrame self) => self.XVector;

    /// <summary>The direction up from the frame: its +Y.</summary>
    [ScriptProperty]
    public static Vector3 UpVector(CFrame self) => self.YVector;

    /// <summary>The frame's X axis in the world.</summary>
    [ScriptProperty]
    public static Vector3 XVector(CFrame self) => self.XVector;

    /// <summary>The frame's Y axis in the world.</summary>
    [ScriptProperty]
    public static Vector3 YVector(CFrame self) => self.YVector;

    /// <summary>The frame's Z axis in the world.</summary>
    [ScriptProperty]
    public static Vector3 ZVector(CFrame self) => self.ZVector;

    /// <summary>The frame that undoes this one.</summary>
    [ScriptMethod]
    public static CFrame Inverse(CFrame self) => self.Inverse();

    /// <summary>The same frame with its rotation brought back to unit length.</summary>
    [ScriptMethod]
    public static CFrame Orthonormalize(CFrame self) => self.Orthonormalize();

    /// <summary>The frame part of the way to the goal: 0 is this frame and 1 is the goal.</summary>
    [ScriptMethod]
    public static CFrame Lerp(CFrame self, CFrame goal, float alpha) => self.Lerp(goal, alpha);

    /// <summary>Whether the other frame is this one, to within epsilon.</summary>
    [ScriptMethod]
    public static bool FuzzyEq(CFrame self, CFrame other, float epsilon = 1e-5f) => self.FuzzyEq(other, epsilon);

    /// <summary>A frame given in this frame's space, in the world. The same as self * cf.</summary>
    [ScriptMethod]
    public static CFrame ToWorldSpace(CFrame self, CFrame cf) => self.ToWorldSpace(cf);

    /// <summary>A frame of the world, in this frame's space.</summary>
    [ScriptMethod]
    public static CFrame ToObjectSpace(CFrame self, CFrame cf) => self.ToObjectSpace(cf);

    /// <summary>A point given in this frame's space, in the world.</summary>
    [ScriptMethod]
    public static Vector3 PointToWorldSpace(CFrame self, Vector3 point) => self.PointToWorldSpace(point);

    /// <summary>A point of the world, in this frame's space.</summary>
    [ScriptMethod]
    public static Vector3 PointToObjectSpace(CFrame self, Vector3 point) => self.PointToObjectSpace(point);

    /// <summary>A direction given in this frame's space, in the world. The position plays no part.</summary>
    [ScriptMethod]
    public static Vector3 VectorToWorldSpace(CFrame self, Vector3 vector) => self.VectorToWorldSpace(vector);

    /// <summary>A direction of the world, in this frame's space. The position plays no part.</summary>
    [ScriptMethod]
    public static Vector3 VectorToObjectSpace(CFrame self, Vector3 vector) => self.VectorToObjectSpace(vector);

    /// <summary>The rotation as angles about X, Y and Z in radians, applied in that order.</summary>
    [ScriptMethod]
    public static (float X, float Y, float Z) ToEulerAnglesXYZ(CFrame self) => Spread(self.ToEulerAnglesXYZ());

    /// <summary>The rotation as angles about X, Y and Z in radians, applied in the order Y, X, Z.</summary>
    [ScriptMethod]
    public static (float X, float Y, float Z) ToEulerAnglesYXZ(CFrame self) => Spread(self.ToEulerAnglesYXZ());

    /// <summary>The same as ToEulerAnglesYXZ.</summary>
    [ScriptMethod]
    public static (float X, float Y, float Z) ToOrientation(CFrame self) => Spread(self.ToOrientation());

    /// <summary>The position, then the nine numbers of the rotation matrix row by row.</summary>
    [ScriptMethod(Returns = TwelveNumbers)]
    public static int GetComponents(LuauStack stack, CFrame self)
    {
        Span<float> components = stackalloc float[CFrame.ComponentCount];
        self.GetComponents(components);
        foreach (float component in components)
        {
            if (!stack.Push(component))
                return LuauStack.Raise;
        }

        return components.Length;
    }

    /// <summary>The same as GetComponents.</summary>
    [ScriptMethod(Name = "components", Returns = TwelveNumbers)]
    [ScriptDeprecated("GetComponents")]
    public static int Components(LuauStack stack, CFrame self) => GetComponents(stack, self);

    // A frame times a frame places the second in the space of the first. A
    // frame times a vector moves the point from the frame's space to the world.
    [ScriptOperator("*", "CFrame", "CFrame")]
    [ScriptOperator("*", "Vector3", "Vector3")]
    public static int Multiply(LuauStack stack)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame left))
            return WrongOperands(stack, "mul");
        if (FrameValue.TryGet(stack, 2, out CFrame right))
            return ScriptResult.Of(FrameValue.Push(stack, left * right));

        return stack.TryGetVector(2, out Vector3 point)
            ? ScriptResult.Of(stack.Push(left * point))
            : WrongOperands(stack, "mul");
    }

    [ScriptOperator("+", "Vector3", "CFrame")]
    public static int Add(LuauStack stack) => Offset(stack, 1f);

    [ScriptOperator("-", "Vector3", "CFrame")]
    public static int Subtract(LuauStack stack) => Offset(stack, -1f);

    private const string TwelveNumbers =
        "(number, number, number, number, number, number, number, number, number, number, number, number)";

    private static (float X, float Y, float Z) Spread(Vector3 angles) => (angles.X, angles.Y, angles.Z);

    private static int Offset(LuauStack stack, float sign)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame frame) || !stack.TryGetVector(2, out Vector3 offset))
            return WrongOperands(stack, sign > 0f ? "add" : "sub");

        return ScriptResult.Of(FrameValue.Push(stack, frame + (offset * sign)));
    }

    // The words Luau itself uses for two values an operator cannot take.
    private static int WrongOperands(LuauStack stack, string operation) =>
        stack.Fail(
            $"attempt to perform arithmetic ({operation}) on " +
            $"{ScriptArguments.TypeName(stack, 1)} and {ScriptArguments.TypeName(stack, 2)}");
}
