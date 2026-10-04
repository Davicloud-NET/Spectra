using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Physics.Box3D.Native;

/// <summary>Box3D's semantic version, as reported by the loaded library.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3Version
{
    public int Major;
    public int Minor;
    public int Revision;

    public override readonly string ToString() => $"{Major}.{Minor}.{Revision}";
}

/// <summary>A three-component vector in Box3D's layout.</summary>
// Not aliased to Vector3: binding types mirror the C library, not the engine.
[StructLayout(LayoutKind.Sequential)]
public struct B3Vec3
{
    public float X;
    public float Y;
    public float Z;

    public B3Vec3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static B3Vec3 From(Vector3 v) => new(v.X, v.Y, v.Z);

    public readonly Vector3 ToVector3() => new(X, Y, Z);
}

/// <summary>A quaternion: vector part first, scalar last.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3Quat
{
    public B3Vec3 V;
    public float S;

    public static B3Quat From(Quaternion q) => new() { V = new B3Vec3(q.X, q.Y, q.Z), S = q.W };

    public readonly Quaternion ToQuaternion() => new(V.X, V.Y, V.Z, S);
}

/// <summary>A rigid transform: translation plus rotation, no scale.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3Transform
{
    public B3Vec3 P;
    public B3Quat Q;
}

/// <summary>A world position.</summary>
// Same bytes as B3Vec3 in the float build, but b3Pos is a struct of doubles
// under BOX3D_DOUBLE_PRECISION. Keep the two types apart.
[StructLayout(LayoutKind.Sequential)]
public struct B3Pos
{
    public float X;
    public float Y;
    public float Z;

    public B3Pos(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static B3Pos From(Vector3 v) => new(v.X, v.Y, v.Z);

    public readonly Vector3 ToVector3() => new(X, Y, Z);
}

/// <summary>A world transform: a <see cref="B3Pos"/> translation and a rotation.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3WorldTransform
{
    public B3Pos P;
    public B3Quat Q;
}

/// <summary>An opaque handle to a physics world. Four bytes, unlike the body and shape ids.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3WorldId
{
    public ushort Index1;
    public ushort Generation;
}

/// <summary>An opaque handle to a body. Index1 is one-based, so <c>default</c> is the null handle.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3BodyId
{
    public int Index1;
    public ushort World0;
    public ushort Generation;
}

/// <summary>An opaque handle to a shape.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3ShapeId
{
    public int Index1;
    public ushort World0;
    public ushort Generation;
}

/// <summary>An axis-aligned bounding box in Box3D's layout.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3Aabb
{
    public B3Vec3 LowerBound;
    public B3Vec3 UpperBound;
}
