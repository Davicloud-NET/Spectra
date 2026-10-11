using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The methods of a CFrame, each a thin call of the Core type's own.
internal static class CFrameMethods
{
    public static MethodTable Build(LuauHost host) =>
        new ApiTable(host)
            .Function("Inverse"u8, static stack => Frame(stack, "Inverse", static self => self.Inverse()))
            .Function("Orthonormalize"u8, static stack => Frame(stack, "Orthonormalize", static self => self.Orthonormalize()))
            .Function("Lerp"u8, Lerp)
            .Function("FuzzyEq"u8, FuzzyEq)
            .Function("ToWorldSpace"u8, static stack => WithFrame(stack, "ToWorldSpace", static (self, other) => self.ToWorldSpace(other)))
            .Function("ToObjectSpace"u8, static stack => WithFrame(stack, "ToObjectSpace", static (self, other) => self.ToObjectSpace(other)))
            .Function("PointToWorldSpace"u8, static stack => WithVector(stack, "PointToWorldSpace", static (self, v) => self.PointToWorldSpace(v)))
            .Function("PointToObjectSpace"u8, static stack => WithVector(stack, "PointToObjectSpace", static (self, v) => self.PointToObjectSpace(v)))
            .Function("VectorToWorldSpace"u8, static stack => WithVector(stack, "VectorToWorldSpace", static (self, v) => self.VectorToWorldSpace(v)))
            .Function("VectorToObjectSpace"u8, static stack => WithVector(stack, "VectorToObjectSpace", static (self, v) => self.VectorToObjectSpace(v)))
            .Function("ToEulerAnglesXYZ"u8, static stack => Angles(stack, "ToEulerAnglesXYZ", static self => self.ToEulerAnglesXYZ()))
            .Function("ToEulerAnglesYXZ"u8, static stack => Angles(stack, "ToEulerAnglesYXZ", static self => self.ToEulerAnglesYXZ()))
            .Function("ToOrientation"u8, static stack => Angles(stack, "ToOrientation", static self => self.ToOrientation()))
            .Function("GetComponents"u8, static stack => Components(stack, "GetComponents"))
            .Function("components"u8, static stack => Components(stack, "components"))
            .KeepAsMethods();

    private static int Frame(LuauStack stack, string method, Func<CFrame, CFrame> apply) =>
        FrameValue.TryGet(stack, 1, out CFrame self)
            ? Push(stack, apply(self))
            : ScriptArguments.NoSelf(stack, method);

    private static int WithFrame(LuauStack stack, string method, Func<CFrame, CFrame, CFrame> apply)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame self))
            return ScriptArguments.NoSelf(stack, method);
        if (!FrameValue.TryGet(stack, 2, out CFrame other))
            return ScriptArguments.Wrong(stack, 2, method, "CFrame", isMethod: true);

        return Push(stack, apply(self, other));
    }

    private static int WithVector(LuauStack stack, string method, Func<CFrame, Vector3, Vector3> apply)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame self))
            return ScriptArguments.NoSelf(stack, method);
        if (!stack.TryGetVector(2, out Vector3 vector))
            return ScriptArguments.Wrong(stack, 2, method, "Vector3", isMethod: true);

        return ScriptResult.Of(stack.Push(apply(self, vector)));
    }

    private static int Lerp(LuauStack stack)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame self))
            return ScriptArguments.NoSelf(stack, "Lerp");
        if (!FrameValue.TryGet(stack, 2, out CFrame goal))
            return ScriptArguments.Wrong(stack, 2, "Lerp", "CFrame", isMethod: true);
        if (!ScriptArguments.TryNumber(stack, 3, out float alpha))
            return ScriptArguments.Wrong(stack, 3, "Lerp", "number", isMethod: true);

        return Push(stack, self.Lerp(goal, alpha));
    }

    private static int FuzzyEq(LuauStack stack)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame self))
            return ScriptArguments.NoSelf(stack, "FuzzyEq");
        if (!FrameValue.TryGet(stack, 2, out CFrame other))
            return ScriptArguments.Wrong(stack, 2, "FuzzyEq", "CFrame", isMethod: true);
        if (!ScriptArguments.TryOptionalNumber(stack, 3, 1e-5, out double epsilon))
            return ScriptArguments.Wrong(stack, 3, "FuzzyEq", "number", isMethod: true);

        return ScriptResult.Of(stack.Push(self.FuzzyEq(other, (float)epsilon)));
    }

    // Three results: the angles about X, Y and Z, in radians.
    private static int Angles(LuauStack stack, string method, Func<CFrame, Vector3> read)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame self))
            return ScriptArguments.NoSelf(stack, method);

        Vector3 angles = read(self);
        return stack.Push(angles.X) && stack.Push(angles.Y) && stack.Push(angles.Z) ? 3 : LuauStack.Raise;
    }

    // Twelve results: the position, then the rotation matrix row by row.
    private static int Components(LuauStack stack, string method)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame self))
            return ScriptArguments.NoSelf(stack, method);

        Span<float> components = stackalloc float[CFrame.ComponentCount];
        self.GetComponents(components);
        foreach (float component in components)
        {
            if (!stack.Push(component))
                return LuauStack.Raise;
        }

        return components.Length;
    }

    private static int Push(LuauStack stack, CFrame frame) => ScriptResult.Of(FrameValue.Push(stack, frame));
}
