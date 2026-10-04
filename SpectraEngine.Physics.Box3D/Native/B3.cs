using System.Runtime.InteropServices;

namespace SpectraEngine.Physics.Box3D.Native;

// Raw Box3D entry points, one-to-one with the C API. Hand-written: only what
// the engine calls. No null checks or lifetime rules here; those live a layer up.
// Runtime marshalling is off, so C bool is marshalled as U1 explicitly.
internal static partial class B3
{
    private const string Lib = "box3d";

    // Every struct here assumes the float build. Check once at startup.
    [LibraryImport(Lib, EntryPoint = "b3IsDoublePrecision")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool IsDoublePrecision();

    [LibraryImport(Lib, EntryPoint = "b3GetVersion")]
    internal static partial B3Version GetVersion();

    // Call before the first Default*Def: those bake the length scale into
    // their results when called.
    [LibraryImport(Lib, EntryPoint = "b3SetLengthUnitsPerMeter")]
    internal static partial void SetLengthUnitsPerMeter(float lengthUnits);

    [LibraryImport(Lib, EntryPoint = "b3GetLengthUnitsPerMeter")]
    internal static partial float GetLengthUnitsPerMeter();

    // Always start a B3WorldDef from this, never from a zeroed struct.
    [LibraryImport(Lib, EntryPoint = "b3DefaultWorldDef")]
    internal static partial B3WorldDef DefaultWorldDef();

    // A zeroed id means failure. In a Release build that is the only signal.
    [LibraryImport(Lib, EntryPoint = "b3CreateWorld")]
    internal static partial B3WorldId CreateWorld(in B3WorldDef def);

    // Once only: the world count is decremented before the id is validated,
    // so a double destroy corrupts it.
    [LibraryImport(Lib, EntryPoint = "b3DestroyWorld")]
    internal static partial void DestroyWorld(B3WorldId worldId);

    [LibraryImport(Lib, EntryPoint = "b3World_IsValid")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool World_IsValid(B3WorldId id);

    // timeStep is the fixed tick, never the frame delta.
    [LibraryImport(Lib, EntryPoint = "b3World_Step")]
    internal static partial void World_Step(B3WorldId worldId, float timeStep, int subStepCount);

    [LibraryImport(Lib, EntryPoint = "b3World_SetGravity")]
    internal static partial void World_SetGravity(B3WorldId worldId, B3Vec3 gravity);

    [LibraryImport(Lib, EntryPoint = "b3World_GetGravity")]
    internal static partial B3Vec3 World_GetGravity(B3WorldId worldId);

    // Expected to be 1: the engine runs Box3D serially.
    [LibraryImport(Lib, EntryPoint = "b3World_GetWorkerCount")]
    internal static partial int World_GetWorkerCount(B3WorldId worldId);

    [LibraryImport(Lib, EntryPoint = "b3World_GetAwakeBodyCount")]
    internal static partial int World_GetAwakeBodyCount(B3WorldId worldId);

    [LibraryImport(Lib, EntryPoint = "b3GetWorldCount")]
    internal static partial int GetWorldCount();

    [LibraryImport(Lib, EntryPoint = "b3World_EnableSleeping")]
    internal static partial void World_EnableSleeping(
        B3WorldId worldId, [MarshalAs(UnmanagedType.U1)] bool flag);

    // Full rebuild of the static broadphase tree. Once per batch, never per shape.
    // Upstream's header calls it "for internal testing", so it may vanish when
    // the pin moves; the tree stays correct without it.
    [LibraryImport(Lib, EntryPoint = "b3World_RebuildStaticTree")]
    internal static partial void World_RebuildStaticTree(B3WorldId worldId);

    [LibraryImport(Lib, EntryPoint = "b3DefaultBodyDef")]
    internal static partial B3BodyDef DefaultBodyDef();

    [LibraryImport(Lib, EntryPoint = "b3CreateBody")]
    internal static partial B3BodyId CreateBody(B3WorldId worldId, in B3BodyDef def);

    [LibraryImport(Lib, EntryPoint = "b3DestroyBody")]
    internal static partial void DestroyBody(B3BodyId bodyId);

    [LibraryImport(Lib, EntryPoint = "b3Body_IsValid")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool Body_IsValid(B3BodyId id);

    [LibraryImport(Lib, EntryPoint = "b3Body_GetType")]
    internal static partial B3BodyType Body_GetType(B3BodyId bodyId);

    [LibraryImport(Lib, EntryPoint = "b3Body_SetType")]
    internal static partial void Body_SetType(B3BodyId bodyId, B3BodyType type);

    [LibraryImport(Lib, EntryPoint = "b3Body_SetUserData")]
    internal static partial void Body_SetUserData(B3BodyId bodyId, nint userData);

    [LibraryImport(Lib, EntryPoint = "b3Body_GetUserData")]
    internal static partial nint Body_GetUserData(B3BodyId bodyId);

    [LibraryImport(Lib, EntryPoint = "b3Body_GetTransform")]
    internal static partial B3WorldTransform Body_GetTransform(B3BodyId bodyId);

    [LibraryImport(Lib, EntryPoint = "b3Body_SetTransform")]
    internal static partial void Body_SetTransform(B3BodyId bodyId, B3Pos position, B3Quat rotation);

    // Velocity-based, so riders and friction behave. The achieved pose is
    // close, not exact: don't write it back into the authored transform.
    [LibraryImport(Lib, EntryPoint = "b3Body_SetTargetTransform")]
    internal static partial void Body_SetTargetTransform(
        B3BodyId bodyId, B3WorldTransform target, float timeStep,
        [MarshalAs(UnmanagedType.U1)] bool wake);

    [LibraryImport(Lib, EntryPoint = "b3Body_GetShapeCount")]
    internal static partial int Body_GetShapeCount(B3BodyId bodyId);

    [LibraryImport(Lib, EntryPoint = "b3Body_ApplyMassFromShapes")]
    internal static partial void Body_ApplyMassFromShapes(B3BodyId bodyId);

    [LibraryImport(Lib, EntryPoint = "b3Body_Enable")]
    internal static partial void Body_Enable(B3BodyId bodyId);

    [LibraryImport(Lib, EntryPoint = "b3Body_Disable")]
    internal static partial void Body_Disable(B3BodyId bodyId);

    [LibraryImport(Lib, EntryPoint = "b3Body_IsEnabled")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool Body_IsEnabled(B3BodyId bodyId);

    // Hull from a point cloud. Null on failure (over a limit, degenerate input).
    // nint because the hull is a variable-length struct that can't be copied.
    [LibraryImport(Lib, EntryPoint = "b3CreateHull")]
    internal static unsafe partial nint CreateHull(B3Vec3* points, int pointCount, int maxVertexCount);

    // Dereferences null.
    [LibraryImport(Lib, EntryPoint = "b3DestroyHull")]
    internal static partial void DestroyHull(nint hull);

    [LibraryImport(Lib, EntryPoint = "b3DefaultShapeDef")]
    internal static partial B3ShapeDef DefaultShapeDef();

    [LibraryImport(Lib, EntryPoint = "b3DefaultFilter")]
    internal static partial B3Filter DefaultFilter();

    [LibraryImport(Lib, EntryPoint = "b3DefaultSurfaceMaterial")]
    internal static partial B3SurfaceMaterial DefaultSurfaceMaterial();

    // Dereferences a null hull, and CreateHull can return null. Check first.
    [LibraryImport(Lib, EntryPoint = "b3CreateHullShape")]
    internal static partial B3ShapeId CreateHullShape(B3BodyId bodyId, in B3ShapeDef def, nint hull);

    // Same null rule as CreateHullShape.
    [LibraryImport(Lib, EntryPoint = "b3CreateTransformedHullShape")]
    internal static partial B3ShapeId CreateTransformedHullShape(
        B3BodyId bodyId, in B3ShapeDef def, nint hull, B3Transform transform, B3Vec3 scale);

    [LibraryImport(Lib, EntryPoint = "b3DestroyShape")]
    internal static partial void DestroyShape(
        B3ShapeId shapeId, [MarshalAs(UnmanagedType.U1)] bool updateBodyMass);

    [LibraryImport(Lib, EntryPoint = "b3Shape_IsValid")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool Shape_IsValid(B3ShapeId id);

    [LibraryImport(Lib, EntryPoint = "b3Shape_SetUserData")]
    internal static partial void Shape_SetUserData(B3ShapeId shapeId, nint userData);

    [LibraryImport(Lib, EntryPoint = "b3Shape_GetUserData")]
    internal static partial nint Shape_GetUserData(B3ShapeId shapeId);

    [LibraryImport(Lib, EntryPoint = "b3Shape_GetBody")]
    internal static partial B3BodyId Shape_GetBody(B3ShapeId shapeId);

    [LibraryImport(Lib, EntryPoint = "b3ComputeHullAABB")]
    internal static partial B3Aabb ComputeHullAABB(nint hull, B3Transform transform);
}
