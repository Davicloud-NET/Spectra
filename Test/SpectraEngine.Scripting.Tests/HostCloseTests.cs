using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>Closing a host: with scripts that wait, from another thread, and from inside a call.</summary>
public sealed class HostCloseTests
{
    [Fact]
    public void A_host_closes_with_a_script_that_waits_and_a_function_that_is_kept()
    {
        LuauFunction? kept = null;
        LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("keep"u8, stack =>
        {
            kept = fresh.KeepFunction(stack, 1);
            return 0;
        }));
        LuauScript script = host.LoadText("keep(function() end)\ncoroutine.yield()\nreturn 'never'");
        script.Start().ShouldBe(LuauResume.Yielded);

        host.Close();

        host.IsClosed.ShouldBeTrue();
        script.Thread.IsAlive.ShouldBeFalse();
        script.Thread.Resume().ShouldBe(LuauResume.Refused);
        kept.ShouldNotBeNull().Start().ShouldBe(LuauResume.Refused);
        kept.Dispose();
        script.SwitchOff();
        host.MemoryUsed.ShouldBe(0);
    }

    [Fact]
    public void A_host_can_be_closed_twice()
    {
        LuauHost host = TestHost.Open(out _);

        host.Close();
        host.Dispose();

        host.IsClosed.ShouldBeTrue();
        Should.Throw<ObjectDisposedException>(() => host.Stack.Count);
    }

    // What the editor does when its engine thread has died: the state was
    // made and used there, and is closed from here.
    [Fact]
    public async Task A_host_used_on_one_thread_is_closed_from_another()
    {
        NativeLuau.Require();
        LuauHost? host = null;
        LuauResume outcome = LuauResume.Refused;
        Thread user = new(() =>
        {
            host = TestHost.Open(out _);
            outcome = host.RunText("coroutine.yield()");
        });
        user.Start();
        user.Join();

        await Task.Run(() => host.ShouldNotBeNull().Close(), TestContext.Current.CancellationToken);

        outcome.ShouldBe(LuauResume.Yielded);
        host.ShouldNotBeNull().IsClosed.ShouldBeTrue();
    }

    // The state is in use while a script runs. The close is taken up when
    // the call that the script runs in has ended.
    [Fact]
    public void A_close_from_inside_a_call_waits_for_the_call_to_end()
    {
        bool wasClosedInside = true;
        LuauHost host = TestHost.Open(out RecordingSink sink, fresh => fresh.SetGlobal("quit"u8, _ =>
        {
            fresh.Close();
            wasClosedInside = fresh.IsClosed;
            return 0;
        }));

        LuauResume outcome = host.RunText("quit()\nprint('the call goes on')");

        wasClosedInside.ShouldBeFalse();
        outcome.ShouldBe(LuauResume.Finished);
        host.IsClosed.ShouldBeTrue();
        sink.Messages.ShouldHaveSingleItem().Text.ShouldBe("the call goes on");
    }

    [Fact]
    public void Many_hosts_open_and_close_with_scripts_left_waiting()
    {
        NativeLuau.Require();
        for (int round = 0; round < 50; round++)
        {
            using LuauHost host = TestHost.Open(out _);
            host.RunText("local held = table.create(1000, 'x')\ncoroutine.yield(held)").ShouldBe(LuauResume.Yielded);
        }
    }
}
