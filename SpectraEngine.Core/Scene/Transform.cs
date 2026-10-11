using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Scene;

public struct Transform
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public Transform()
    {
        Position = Vector3.Zero;
        Rotation = Quaternion.Identity;
        Scale = Vector3.One;
    }

    public static Transform Identity => new();

    public readonly Matrix4x4 Model =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) *
        Matrix4x4.CreateTranslation(Position);

    // Stricter than ==, which takes -0 for 0. A save writes the sign.
    internal readonly bool HasBitsOf(in Transform other) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<Transform>(in this))
            .SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<Transform>(in other)));
}
