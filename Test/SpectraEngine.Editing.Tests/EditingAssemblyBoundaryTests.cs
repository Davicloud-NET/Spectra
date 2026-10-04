using SpectraEngine.Editing.Input;
using System;
using System.Linq;
using System.Reflection;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// SpectraEngine.Editing must not depend on the windowing or input backend.
/// </summary>
// Silk.NET is reachable transitively through Core, so the compiler won't stop it.
public sealed class EditingAssemblyBoundaryTests
{
    [Fact]
    public void The_editing_assembly_references_no_backend_assembly()
    {
        Assembly editing = typeof(EditorInputFrame).Assembly;

        // GetReferencedAssemblies lists only what the assembly uses, not what
        // was available to it.
        string[] offenders = editing.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name =>
                name.StartsWith("Silk.NET", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Silk.", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"SpectraEngine.Editing must stay backend-free but references: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_editing_assembly_still_references_the_engine_core()
    {
        // Guard: an empty reference list would make the test above pass for nothing.
        Assembly editing = typeof(EditorInputFrame).Assembly;

        editing.GetReferencedAssemblies()
            .Any(reference => reference.Name == "SpectraEngine.Core")
            .ShouldBeTrue();
    }
}
