using System.Runtime.InteropServices;

namespace SpectraEngine.Physics.Box3D.Native;

/// <summary>What drives a body's transform.</summary>
public enum B3BodyType
{
    /// <summary>Never moves.</summary>
    Static = 0,

    /// <summary>Moved by the scene through target transforms, not by the solver.</summary>
    Kinematic = 1,

    /// <summary>Moved by the solver. The only kind written back into the scene.</summary>
    Dynamic = 2,
}

/// <summary>Per-axis motion constraints. Each field is a one-byte C bool.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3MotionLocks
{
    public byte LinearX;
    public byte LinearY;
    public byte LinearZ;
    public byte AngularX;
    public byte AngularY;
    public byte AngularZ;
}

/// <summary>
/// The parameters a body is created from. Take it from <c>b3DefaultBodyDef()</c>
/// and change fields; never build one from scratch.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3BodyDef
{
    public B3BodyType Type;
    public B3Pos Position;
    public B3Quat Rotation;
    public B3Vec3 LinearVelocity;
    public B3Vec3 AngularVelocity;
    public float LinearDamping;
    public float AngularDamping;
    public float GravityScale;
    public float SleepThreshold;

    /// <summary>Borrowed <c>const char*</c> that Box3D does not copy. Keep null.</summary>
    public nint Name;

    /// <summary>The engine's back-reference: a dense index plus one, not a pinned handle.</summary>
    public nint UserData;

    public B3MotionLocks MotionLocks;
    public byte EnableSleep;
    public byte IsAwake;
    public byte IsBullet;
    public byte IsEnabled;
    public byte AllowFastRotation;
    public byte EnableContactRecycling;

    /// <summary>Box3D's construction cookie. Never assign.</summary>
    public int InternalValue;
}

/// <summary>Collision filtering bits: 64 categories.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3Filter
{
    public ulong CategoryBits;
    public ulong MaskBits;
    public int GroupIndex;
}

/// <summary>Surface response for one shape. A hull has one, so no per-face friction.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3SurfaceMaterial
{
    public float Friction;
    public float Restitution;
    public float RollingResistance;
    public B3Vec3 TangentVelocity;
    public ulong UserMaterialId;
    public uint CustomColor;

    /// <summary>Tail padding from the C struct. Never read.</summary>
    public uint Padding;
}

/// <summary>
/// The parameters a shape is created from. Take it from <c>b3DefaultShapeDef()</c>
/// and change fields.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3ShapeDef
{
    /// <summary>Borrowed <c>const char*</c> that Box3D does not copy. Keep null.</summary>
    public nint Name;

    public nint UserData;

    /// <summary>Borrowed array pointer. Keep null and use <see cref="BaseMaterial"/>.</summary>
    public nint Materials;

    public int MaterialCount;
    public B3SurfaceMaterial BaseMaterial;
    public float Density;
    public float ExplosionScale;
    public B3Filter Filter;
    public byte EnableCustomFiltering;
    public byte IsSensor;
    public byte EnableSensorEvents;
    public byte EnableContactEvents;
    public byte EnableHitEvents;
    public byte EnablePreSolveEvents;
    public byte InvokeContactCreation;

    /// <summary>
    /// Recompute the body's mass when this shape is added. Turn it off when
    /// adding many shapes to one body.
    /// </summary>
    public byte UpdateBodyMass;

    public byte EnableSpeculativeContact;

    /// <summary>Box3D's construction cookie. Never assign.</summary>
    public int InternalValue;
}
