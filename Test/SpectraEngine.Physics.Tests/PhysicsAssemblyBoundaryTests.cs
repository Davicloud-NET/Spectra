using System;
using System.Linq;
using System.Reflection;
using SpectraEngine.Physics.Box3D;
using SpectraEngine.Physics.Box3D.Native;

namespace SpectraEngine.Physics.Tests;

/// <summary>
/// The physics binding must not depend on the windowing or input backend, so
/// it can run headless on a dedicated server.
/// </summary>
// Silk.NET comes in transitively through SpectraEngine.Core, so the compiler
// won't stop a stray using.
public sealed class PhysicsAssemblyBoundaryTests
{
    [Fact]
    public void The_physics_assembly_references_no_backend_assembly()
    {
        Assembly physics = typeof(BrushHullBuilder).Assembly;

        // Only references the metadata uses, not everything available.
        string[] offenders = physics.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Silk.", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.ShouldBeEmpty(
            "SpectraEngine.Physics.Box3D must stay headless-capable but references: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void The_physics_assembly_still_references_the_engine_core()
    {
        // Guards the test above against passing on an empty reference list.
        Assembly physics = typeof(BrushHullBuilder).Assembly;

        physics.GetReferencedAssemblies()
            .Any(reference => reference.Name == "SpectraEngine.Core")
            .ShouldBeTrue();
    }

    [Fact]
    public void Runtime_marshalling_is_disabled_for_the_binding()
    {
        // With the attribute a non-blittable P/Invoke is a compile error. Without
        // it the marshaller widens a bool in a struct to four bytes and shifts
        // every field after it, and no other test here would notice.
        Assembly physics = typeof(B3Vec3).Assembly;

        physics.GetCustomAttributes()
            .Any(a => a.GetType().Name == "DisableRuntimeMarshallingAttribute")
            .ShouldBeTrue(
                "SpectraEngine.Physics.Box3D must keep [assembly: DisableRuntimeMarshalling] — " +
                "without it, a layout mistake stops being a compile error");
    }
}
