using Microsoft.Extensions.Logging;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What the level's log says when the queue has no room, or a tick no time,
/// for the call of a listener of a signal a script set off. No thread waits
/// on such a call and no script gets an error, so the line says neither.
/// </summary>
public sealed class DeferredCallLogTests : IDisposable
{
    private const float Tick = 1f / 60f;

    private const string FiveListeners = """
        local board = workspace.Board
        for index = 1, 5 do
            board.AttributeChanged:Connect(function(name) print("heard", index, name) end)
        end

        """;

    private readonly CapturingLogger _logger = new();
    private readonly RecordingSink _sink = new();
    private EntityWorld? _world;

    [Fact]
    public void A_listener_the_queue_has_no_room_for_is_logged_as_a_call_that_was_dropped()
    {
        EntityWorld world = Playing(FiveListeners + "board:SetAttribute('Score', 1)");
        world.MaxPendingEvents = 2;

        world.Tick(Tick);

        _sink.Texts(ScriptSeverity.Error).ShouldBeEmpty();
        _sink.Texts(ScriptSeverity.Print).ShouldBe(["heard 1 Score", "heard 2 Score"]);
        string error = _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        error.ShouldContain("a listener's call for 'Signal AttributeChanged'");
        error.ShouldNotContain("threads are waiting");
        error.ShouldNotContain("The script that asked gets an error");
    }

    [Fact]
    public void Listeners_that_wait_past_a_ticks_budget_are_logged_as_calls_that_run_late()
    {
        EntityWorld world = Playing(FiveListeners + """
            local connection
            connection = game:GetService("RunService").Heartbeat:Connect(function()
                connection:Disconnect()
                board:SetAttribute("Score", 1)
            end)
            """);
        world.Tick(Tick);
        world.MaxDispatchesPerTick = 2;

        // Heartbeat runs outside a drain, so the five calls wait for the next one.
        world.Scripts.ShouldNotBeNull().AfterSimulation();
        world.Tick(Tick);

        _sink.Texts(ScriptSeverity.Print).ShouldBe(["heard 1 Score", "heard 2 Score"]);
        string warning = _logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain("a listener's call for 'Signal AttributeChanged'");
        warning.ShouldNotContain("script threads");

        world.Tick(Tick);
        world.Tick(Tick);
        _sink.Texts(ScriptSeverity.Error).ShouldBeEmpty();
        _sink.Texts(ScriptSeverity.Print).Length.ShouldBe(5);
        _logger.MessagesAt(LogLevel.Error).ShouldBeEmpty();
    }

    public void Dispose()
    {
        if (_world is not { } world)
            return;

        world.Deactivate();
        world.Scripts?.Close();
    }

    // A level with no session over it: one script, and a log a test can read.
    private EntityWorld Playing(string script)
    {
        NativeLuau.Require();

        var scene = new Scene("Level");
        scene.Root.CreateChild("Board");
        var files = new TextScriptSource();
        files.Add("Scripts/main.luau", script);
        scene.Root.CreateChild("Main").Script = new Script("Scripts/main.luau");

        _world = new EntityWorld(scene, _logger, new EntityCatalog()) { FixedDeltaTime = Tick };
        _world.Scripts = new LuauScriptRuntime(new ScriptRuntimeSetup(_world, _sink, files));
        _world.Activate();
        return _world;
    }
}
