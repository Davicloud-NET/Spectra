using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The global CFrame: the ways a script makes one, with Roblox's names.
internal static class CFrameLibrary
{
    private const string NewTakes =
        "CFrame.new takes nothing, a position, a position and a point to look at, or 3, 7 or 12 numbers";

    public static void Install(LuauHost host) =>
        new ApiTable(host)
            .Function("new"u8, New)
            .Function("Angles"u8, static stack => FromAngles(stack, "Angles", CFrame.Angles))
            .Function("fromEulerAnglesXYZ"u8, static stack => FromAngles(stack, "fromEulerAnglesXYZ", CFrame.Angles))
            .Function("fromEulerAnglesYXZ"u8, static stack => FromAngles(stack, "fromEulerAnglesYXZ", CFrame.FromEulerAnglesYXZ))
            .Function("fromOrientation"u8, static stack => FromAngles(stack, "fromOrientation", CFrame.FromOrientation))
            .Function("lookAt"u8, static stack => Look(stack, "lookAt", CFrame.LookAt))
            .Function("lookAlong"u8, static stack => Look(stack, "lookAlong", CFrame.LookAlong))
            .Function("fromAxisAngle"u8, FromAxisAngle)
            .Function("fromMatrix"u8, FromMatrix)
            .Frame("identity"u8, CFrame.Identity)
            .SetAsGlobal("CFrame"u8);

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

    private static int FromAngles(LuauStack stack, string function, Func<float, float, float, CFrame> make)
    {
        Span<float> angles = [0f, 0f, 0f];
        for (int index = 1; index <= angles.Length; index++)
        {
            if (!ScriptArguments.TryNumber(stack, index, out angles[index - 1]))
                return ScriptArguments.Wrong(stack, index, function, "number");
        }

        return Push(stack, make(angles[0], angles[1], angles[2]));
    }

    // A place, where to look from there, and which way is up when it is not +Y.
    private static int Look(LuauStack stack, string function, Func<Vector3, Vector3, Vector3, CFrame> make)
    {
        if (!stack.TryGetVector(1, out Vector3 at))
            return ScriptArguments.Wrong(stack, 1, function, "Vector3");
        if (!stack.TryGetVector(2, out Vector3 toward))
            return ScriptArguments.Wrong(stack, 2, function, "Vector3");
        if (!ScriptArguments.TryOptionalVector(stack, 3, Vector3.UnitY, out Vector3 up))
            return ScriptArguments.Wrong(stack, 3, function, "Vector3");

        return Push(stack, make(at, toward, up));
    }

    private static int FromAxisAngle(LuauStack stack)
    {
        if (!stack.TryGetVector(1, out Vector3 axis))
            return ScriptArguments.Wrong(stack, 1, "fromAxisAngle", "Vector3");
        if (!ScriptArguments.TryNumber(stack, 2, out float angle))
            return ScriptArguments.Wrong(stack, 2, "fromAxisAngle", "number");

        return Push(stack, CFrame.FromAxisAngle(axis, angle));
    }

    // The third column may be left out: a rotation's follows from the first two.
    private static int FromMatrix(LuauStack stack)
    {
        Span<Vector3> columns = [default, default, default];
        for (int index = 1; index <= columns.Length; index++)
        {
            if (!stack.TryGetVector(index, out columns[index - 1]))
                return ScriptArguments.Wrong(stack, index, "fromMatrix", "Vector3");
        }

        return ScriptArguments.TryOptionalVector(stack, 4, default, out _)
            ? Push(stack, CFrame.FromMatrix(columns[0], columns[1], columns[2]))
            : ScriptArguments.Wrong(stack, 4, "fromMatrix", "Vector3");
    }

    private static int Push(LuauStack stack, CFrame frame) => ScriptResult.Of(FrameValue.Push(stack, frame));
}
