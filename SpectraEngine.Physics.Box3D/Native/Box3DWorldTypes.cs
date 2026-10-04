using System;
using System.Runtime.InteropServices;

namespace SpectraEngine.Physics.Box3D.Native;

/// <summary>Pre-sized capacities for a world's internal arrays.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct B3Capacity
{
    public int StaticShapeCount;
    public int DynamicShapeCount;
    public int StaticBodyCount;
    public int DynamicBodyCount;
    public int ContactCount;
}

/// <summary>
/// The parameters a physics world is created from. Take it from
/// <c>b3DefaultWorldDef()</c> and change fields; never build one from scratch.
/// </summary>
// Box3D checks InternalValue only in Debug, so a hand-built def is accepted in
// Release with unvalidated fields. The default def is serial (WorkerCount 0, no
// task callbacks). C bool fields are byte: one byte each in the C layout.
[StructLayout(LayoutKind.Sequential)]
public struct B3WorldDef
{
    public B3Vec3 Gravity;
    public float RestitutionThreshold;
    public float HitEventThreshold;
    public float ContactHertz;
    public float ContactDampingRatio;
    public float ContactSpeed;
    public float MaximumLinearSpeed;

    /// <summary>Optional friction-mixing callback.</summary>
    public nint FrictionCallback;

    /// <summary>Optional restitution-mixing callback.</summary>
    public nint RestitutionCallback;

    public byte EnableSleep;

    public byte EnableContinuous;

    public uint WorkerCount;

    /// <summary>Task-system callback. Keep both null for a serial world.</summary>
    public nint EnqueueTask;

    /// <inheritdoc cref="EnqueueTask"/>
    public nint FinishTask;

    public nint UserTaskContext;
    public nint UserData;
    public nint CreateDebugShape;
    public nint DestroyDebugShape;
    public nint UserDebugShapeContext;
    public B3Capacity Capacity;

    /// <summary>Box3D's construction cookie, set by <c>b3DefaultWorldDef()</c>. Never assign.</summary>
    public int InternalValue;
}
