using SpectraEngine.Editing.Gizmos;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// What the default key table deliberately does NOT bind.
/// </summary>
public sealed class GizmoShortcutDefaultsTests
{
    [Fact]
    public void No_default_key_cycles_the_tool()
    {
        // GizmoCommand.CycleMode is implemented, documented and bound by
        // nothing - and it stays that way rather than being deleted, because
        // this assembly is a library whose whole point is that re-hosting is a
        // swap of the input adapter: GizmoController implements it correctly and
        // GizmoShortcuts.Defaults is documented as a default another host may
        // ignore. Deleting a correct, implemented, host-facing verb because one
        // shell does not bind it is the worse trade.
        //
        // What this shell does not need is the cycle: three tools already have
        // two key rows, W/E/R and 2/3/4, so a cycling key would be a third way
        // to reach the same three modes. A refusal a test holds cannot rot into
        // an oversight.
        GizmoShortcuts.Defaults
            .Where(pair => pair.Value == GizmoCommand.CycleMode)
            .Select(pair => pair.Key)
            .ShouldBeEmpty("the tools have two key rows already; a cycle would be a third");
    }
}
