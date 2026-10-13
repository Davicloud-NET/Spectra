using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A level taken from another thread while its mover's call has not
/// returned, which is what the editor does after its engine thread died.
/// </summary>
public sealed class LuauMoverCloseTests
{
    private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public void A_level_taken_under_the_movers_call_leaves_that_tick_to_the_built_in_mover_and_says_nothing()
    {
        using var builtIn = new MoverLevel();
        using var level = new MoverLevel();
        using var sink = new BlockingSink();
        level.Manager.Scripts.Sink = sink;
        level.WithScript("""
            local walk = require("./movers/walk")
            local ticks = 0
            game:GetService("Players").LocalPlayer.Character:SetMover(function(state, ...)
                ticks += 1
                if ticks == 5 then
                    print("block")
                    state.Position += Vector3.new(0, 50, 0)
                end
                walk(state, ...)
            end)
            """);
        CharacterCommand ahead = MoverCourse.Walk(0f);
        level.Play().Tick(in ahead, 4);
        CharacterState before = level.State;

        // The level's own thread, which stops inside the mover's print.
        var ticking = new Thread(() => level.Tick(in ahead));
        ticking.Start();
        sink.Blocked.Wait(_deadline, TestContext.Current.CancellationToken)
            .ShouldBeTrue("the mover never reached its print");

        // What the editor does once the engine's thread is gone.
        var taking = new Thread(() => level.Manager.TakeAuthoredMap());
        taking.Start();
        bool wasTaken = taking.Join(_deadline);

        // Let the held thread end before anything is asserted, so a failure
        // leaves no thread behind.
        sink.LetGo();
        ticking.Join(_deadline).ShouldBeTrue("the held tick never ended");

        wasTaken.ShouldBeTrue("taking the level waited for the mover's call");
        level.Runtime.ShouldNotBeNull().IsClosed.ShouldBeTrue();
        sink.Errors.ShouldBeEmpty();
        level.Character.Mover.ShouldBeSameAs(DefaultCharacterMover.Instance);

        // The built-in mover's tick, from where the tick began: what the
        // mover made of its copy after the level was taken is dropped.
        builtIn.Play().Tick(in ahead, 4);
        builtIn.Character.Restore(in before);
        builtIn.Tick(in ahead);
        StateBits.Of(level.State, level.Scene).ShouldBe(StateBits.Of(builtIn.State, builtIn.Scene));
    }
}
