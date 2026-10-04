using SpectraEngine.Editing.Gizmos;

namespace SpectraEngine.Editing.Tests;

/// <summary>What the default key table leaves unbound.</summary>
public sealed class GizmoShortcutDefaultsTests
{
    [Fact]
    public void No_default_key_cycles_the_tool()
    {
        // CycleMode stays in the API for other hosts to bind. Don't delete it.
        GizmoShortcuts.Defaults
            .Where(pair => pair.Value == GizmoCommand.CycleMode)
            .Select(pair => pair.Key)
            .ShouldBeEmpty("the tools have two key rows already; a cycle would be a third");
    }
}
