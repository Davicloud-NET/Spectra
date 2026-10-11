using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A script the host switches off, as it will for one that left the level:
/// nothing of it runs again, and what it held can be freed.
/// </summary>
public sealed class SwitchOffTests
{
    private const int DoorTag = 1;

    [Fact]
    public void A_script_that_is_switched_off_does_not_start()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        LuauScript script = host.LoadText("print('ran')");

        script.SwitchOff();
        LuauResume outcome = script.Start();

        outcome.ShouldBe(LuauResume.Refused);
        script.IsSwitchedOff.ShouldBeTrue();
        sink.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void Switching_a_script_off_ends_its_waiting_threads_and_lets_its_functions_go()
    {
        LuauFunction? kept = null;
        using LuauHost host = TestHost.Open(out RecordingSink sink, fresh => fresh.SetGlobal("keep"u8, stack =>
        {
            kept = fresh.KeepFunction(stack, 1);
            return 0;
        }));
        LuauScript script = host.LoadText("keep(function() coroutine.yield() print('handler went on') end)\ncoroutine.yield()\nprint('top went on')");
        script.Start().ShouldBe(LuauResume.Yielded);
        kept.ShouldNotBeNull().Start([], out LuauThread handler).ShouldBe(LuauResume.Yielded);

        script.SwitchOff();

        script.Thread.IsAlive.ShouldBeFalse();
        handler.IsAlive.ShouldBeFalse();
        handler.Resume().ShouldBe(LuauResume.Refused);
        kept.Start().ShouldBe(LuauResume.Refused);
        sink.Messages.ShouldBeEmpty();
    }

    // What the script held goes when Luau collects: here, an object of the host's.
    [Fact]
    public void What_a_switched_off_script_held_is_freed()
    {
        using LuauHost host = TestHost.Open(out _, fresh =>
        {
            fresh.Stack.NewTable().ShouldBeTrue();
            fresh.Objects.SetMetatable(fresh.Stack, DoorTag).ShouldBeTrue();
        });
        LuauScript script = host.LoadText("held = ...\ncoroutine.yield()");
        script.Start([LuauValue.Object(DoorTag, new object())]).ShouldBe(LuauResume.Yielded);
        host.Collect();
        int before = host.Objects.Count;

        script.SwitchOff();
        host.Collect();

        before.ShouldBe(1);
        host.Objects.Count.ShouldBe(0);
    }

    // A thread that is running cannot be ended. It is ended when it next waits.
    [Fact]
    public void A_script_switched_off_from_inside_its_own_call_ends_when_it_next_waits()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink, fresh => fresh.SetGlobal("leave"u8, stack =>
        {
            fresh.ScriptOf(stack).ShouldNotBeNull().SwitchOff();
            return 0;
        }));
        LuauScript script = host.LoadText("leave()\nprint('still in the same call')\ncoroutine.yield()\nprint('never')");

        LuauResume outcome = script.Start();

        outcome.ShouldBe(LuauResume.Yielded);
        script.Thread.IsAlive.ShouldBeFalse();
        script.Thread.Resume().ShouldBe(LuauResume.Refused);
        sink.Texts(ScriptSeverity.Print).ShouldBe(["still in the same call"]);
    }

    // A tag with no metatable is the host's own mistake. It is said out loud
    // and no script pays for it.
    [Fact]
    public void A_value_the_host_cannot_hand_over_is_reported_and_nothing_runs()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        LuauScript script = host.LoadText("print('ran')");

        LuauResume outcome = script.Start([LuauValue.Object(DoorTag, new object())]);

        outcome.ShouldBe(LuauResume.Refused);
        ScriptMessage message = sink.Single;
        message.Severity.ShouldBe(ScriptSeverity.Error);
        message.Text.ShouldBe("the host could not hand a value to a script: the userdata tag does not stand for managed objects");
        script.Thread.IsAlive.ShouldBeTrue();
        host.Stack.Count.ShouldBe(0);
    }
}
