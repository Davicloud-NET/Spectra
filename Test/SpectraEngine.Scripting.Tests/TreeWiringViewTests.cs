using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a wiring view is told when a level's tree changes while it is
/// watched: a script whose node is destroyed or leaves has no wire left, a
/// script that came in with a clone has wires of its own, and one that
/// comes back has them again.
/// </summary>
public sealed class TreeWiringViewTests
{
    // Listens to the echo and sends it a ping on every tick.
    private const string Pinger = """
        local echo = workspace.Echo
        echo.OnPing:Connect(function() end)
        while true do
            echo:Ping()
            task.wait()
        end
        """;

    private readonly WireActivityTrace _trace = new();

    [Fact]
    public void A_script_whose_node_is_destroyed_has_no_wire_left()
    {
        using var level = Watched();
        SceneNode crate = level.Part("Crate", new Vector3(0f, 2f, 5f));
        level.ScriptAt("Scripts/pinger.luau", Pinger, under: crate);
        level.ScriptAt("Scripts/main.luau", """
            task.wait(0.05)
            workspace.Crate:Destroy()
            """);

        level.Play().Tick(2);
        Captured(level).Scripts.Wires.Count.ShouldBe(2);

        level.Tick(4);

        level.Errors.ShouldBeEmpty();
        Captured(level).Scripts.Wires.ShouldBeEmpty();

        // Its listener is let go as any dead one is: when the output fires next.
        level.Entities.QueueInput("Echo", "Ping");
        level.Tick(2);
        level.Entities.Listeners.Count.ShouldBe(0);
    }

    [Fact]
    public void A_script_that_came_in_with_a_clone_has_wires_from_its_own_node()
    {
        using var level = Watched();
        SceneNode crate = level.Part("Crate", new Vector3(0f, 2f, 5f));
        SceneNode first = level.ScriptAt("Scripts/pinger.luau", Pinger, under: crate);
        level.ScriptAt("Scripts/main.luau", """
            local copy = workspace.Crate:Clone()
            copy.Name = "Copy"
            copy.Parent = workspace
            """);

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        SceneNode second = level.Scene.Root.Children.Single(node => node.Name == "Copy").Children.Single();
        Guid[] scripts = [.. Captured(level).Scripts.Wires.Select(wire => wire.Script).Distinct()];
        scripts.ShouldBe([first.Id, second.Id], ignoreOrder: true);
        Captured(level).Scripts.Wires.Count.ShouldBe(4);
    }

    [Fact]
    public void A_script_whose_node_left_and_came_back_has_its_wires_again()
    {
        using var level = Watched();
        SceneNode crate = level.Part("Crate", new Vector3(0f, 2f, 5f));
        SceneNode pinger = level.ScriptAt("Scripts/pinger.luau", Pinger, under: crate);
        level.ScriptAt("Scripts/main.luau", """
            local crate = workspace.Crate
            task.wait(0.05)
            crate.Parent = nil
            task.wait(0.05)
            crate.Parent = workspace
            """);

        level.Play().Tick(5);
        Captured(level).Scripts.Wires.ShouldBeEmpty();
        level.Entities.StoppedScripts.Contains(pinger.Id).ShouldBeTrue();

        level.Tick(5);

        level.Errors.ShouldBeEmpty();
        level.Entities.StoppedScripts.Contains(pinger.Id).ShouldBeFalse();
        LogicScriptWire[] wires = [.. Captured(level).Scripts.Wires];
        wires.Length.ShouldBe(2);
        wires.ShouldAllBe(wire => wire.Script == pinger.Id);
    }

    private ScriptLevel Watched()
    {
        var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Echo").Entity = new EntityData(EchoEntity.Class);
        level.Manager.EntityTrace = _trace;
        return level;
    }

    private LogicPlayInfo Captured(ScriptLevel level) => _trace.Capture(null, [], level.Entities);
}
