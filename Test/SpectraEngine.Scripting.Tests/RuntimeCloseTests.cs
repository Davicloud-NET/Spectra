using System.Numerics;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The ways a level's scripts end: Stop, a map load, and the path the editor
/// takes after the thread that ran the level died.
/// </summary>
public sealed class RuntimeCloseTests
{
    private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public void Taking_the_level_from_another_thread_does_not_wait_for_a_call_that_never_returned()
    {
        using var level = new ScriptLevel();
        using var sink = new BlockingSink();
        level.Manager.Scripts.Sink = sink;
        SceneNode part = level.Part("Mover", new Vector3(0f, 2f, 0f));
        level.AddScript(part, """
            script.Parent.Position = Vector3.new(9, 9, 9)
            print('block')
            script.Parent.Position = Vector3.new(1, 1, 1)
            """);

        // The level's own thread, which stops inside the script's print.
        level.Play();
        var ticking = new Thread(() => level.Tick());
        ticking.Start();
        sink.Blocked.Wait(_deadline, TestContext.Current.CancellationToken)
            .ShouldBeTrue("the script never reached its print");

        // What the editor does once the engine's thread is gone.
        MapDocument? taken = null;
        var taking = new Thread(() => taken = level.Manager.TakeAuthoredMap());
        taking.Start();
        bool wasTaken = taking.Join(_deadline);

        // Let the held thread end before anything is asserted, so a failure
        // leaves no thread behind.
        sink.LetGo();
        ticking.Join(_deadline).ShouldBeTrue("the held tick never ended");

        wasTaken.ShouldBeTrue("taking the level waited for the script's call");
        taken.ShouldNotBeNull();
        level.Manager.EntityWorld.ShouldBeNull();
        level.Runtime.ShouldNotBeNull().IsClosed.ShouldBeTrue();
        part.LocalPosition.ShouldBe(new Vector3(0f, 2f, 0f));
    }

    [Theory]
    [InlineData("task.defer(coroutine.create(function(word) print(word) end), 'block')")]
    [InlineData("task.delay(0, coroutine.create(function(word) print(word) end), 'block')")]
    [InlineData("print('block')")]
    public void A_tick_goes_on_and_reports_nothing_after_the_call_that_closed_the_state(string blocking)
    {
        using var level = new ScriptLevel();
        using var sink = new BlockingSink();
        level.Manager.Scripts.Sink = sink;
        level.AddScript(level.Scene.Root, blocking);
        level.AddScript(level.Scene.Root, "print('never started')");

        // The close is asked for while the first script's call has not returned.
        level.Play();
        var ticking = new Thread(() => level.Tick(2));
        ticking.Start();
        sink.Blocked.Wait(_deadline, TestContext.Current.CancellationToken)
            .ShouldBeTrue("the script never reached its print");
        var taking = new Thread(() => level.Manager.TakeAuthoredMap());
        taking.Start();
        bool wasTaken = taking.Join(_deadline);

        sink.LetGo();
        ticking.Join(_deadline).ShouldBeTrue("the held tick never ended");

        wasTaken.ShouldBeTrue("taking the level waited for the script's call");
        level.Runtime.ShouldNotBeNull().IsClosed.ShouldBeTrue();
        sink.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_scene_replaced_during_play_closes_the_runtime()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            game:GetService("RunService").Heartbeat:Connect(function() print('beat') end)
            """);
        level.Play().Tick(2);

        level.Manager.OnSceneReplaced();
        level.Tick(3);

        level.Runtime.ShouldNotBeNull().IsClosed.ShouldBeTrue();
        level.Printed.ShouldBe(["beat", "beat"]);
    }

    [Fact]
    public void Closing_twice_and_calling_a_closed_runtime_do_nothing()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, "print('started')");
        level.Play().Tick();
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();

        level.Stop();
        runtime.Close();
        runtime.StartScripts();
        runtime.Wake(0);
        runtime.Drop(0);
        runtime.BeforeSimulation();
        runtime.AfterSimulation();

        level.Printed.ShouldBe(["started"]);
        level.Errors.ShouldBeEmpty();
    }
}
