using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System.Numerics;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// An attribute's value as the numbers a panel edits one at a time. Three
/// numbers are three parts. A position and rotation is six: the position,
/// then the rotation in degrees, as a node's own Rotation row shows one.
/// Every other kind is edited whole.
/// </summary>
public static class AttributeParts
{
    /// <summary>How many parts a value of that kind has, or 0 for a kind that is edited whole.</summary>
    public static int CountOf(MemberValueKind kind) => kind switch
    {
        MemberValueKind.Vector3 => 3,
        MemberValueKind.CFrame => 6,
        _ => 0,
    };

    /// <summary>One part of a value.</summary>
    public static float Get(in MemberValue value, int part)
    {
        if (value.Kind == MemberValueKind.Vector3)
            return Axis(value.Vector, part);

        return part < 3
            ? Axis(value.Frame.Position, part)
            : Axis(EulerAngles.FromQuaternion(value.Frame.Rotation).AsDegrees, part - 3);
    }

    /// <summary>The value with one part replaced and the others as they were.</summary>
    public static MemberValue With(in MemberValue value, int part, float number)
    {
        if (value.Kind == MemberValueKind.Vector3)
            return MemberValue.Of(WithAxis(value.Vector, part, number));

        CFrame frame = value.Frame;
        if (part < 3)
            return MemberValue.Of(new CFrame(WithAxis(frame.Position, part, number), frame.Rotation));

        // In degrees: a quaternion has no part to leave alone.
        Vector3 degrees = WithAxis(EulerAngles.FromQuaternion(frame.Rotation).AsDegrees, part - 3, number);
        return MemberValue.Of(new CFrame(frame.Position, EulerAngles.FromDegrees(degrees).ToQuaternion()));
    }

    /// <summary>What a new attribute of that kind holds until somebody edits it.</summary>
    public static MemberValue DefaultOf(MemberValueKind kind) => kind switch
    {
        MemberValueKind.Text => MemberValue.Of(string.Empty),
        MemberValueKind.Boolean => MemberValue.Of(false),
        MemberValueKind.Number => MemberValue.Of(0d),
        MemberValueKind.Vector3 => MemberValue.Of(Vector3.Zero),
        MemberValueKind.Color => MemberValue.Of(new Color3(1f, 1f, 1f)),
        MemberValueKind.CFrame => MemberValue.Of(CFrame.Identity),
        _ => MemberValue.None,
    };

    private static float Axis(Vector3 vector, int axis) => axis switch
    {
        0 => vector.X,
        1 => vector.Y,
        _ => vector.Z,
    };

    private static Vector3 WithAxis(Vector3 vector, int axis, float number) => axis switch
    {
        0 => vector with { X = number },
        1 => vector with { Y = number },
        _ => vector with { Z = number },
    };
}
