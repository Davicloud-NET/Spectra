using SpectraEngine.Core;
using SpectraEngine.Core.Maps;
using SpectraEngine.Editor.Shell;
using System;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// What the shell does when an engine session dies: which level the next one
/// is given, and when it stops restarting.
/// </summary>
public sealed class SessionRecoveryTests
{
    private static readonly EngineFault DeviceLost = new("the device went away", IsDeviceLoss: true);
    private static readonly EngineFault Crash = new("something threw", IsDeviceLoss: false);

    private static readonly TimeSpan Start = TimeSpan.FromMinutes(10);

    private static SessionLaunch FromDisk(string path = @"C:\Game\Maps\Lobby.smap") => new(null, null, path);

    private static SessionLaunch Kept(string? loss = null) => new(null, null, null, new MapDocument(), loss);

    // A recovery whose session has started and shown its level.
    private static SessionRecovery Running()
    {
        var recovery = new SessionRecovery();
        recovery.Launched(FromDisk());
        recovery.LevelShown();
        return recovery;
    }

    private static Func<SessionLaunch> Never =>
        () => throw new InvalidOperationException("the dead session's scene must not be read");

    [Fact]
    public void The_first_death_restarts_with_the_level_taken_from_the_dead_session()
    {
        SessionRecovery recovery = Running();
        SessionLaunch taken = Kept();

        recovery.Died(Start, DeviceLost, wasPlaying: false, () => taken).ShouldBe(SessionFaultAction.Restart);

        recovery.Pending.ShouldBeSameAs(taken);
        recovery.IsStopped.ShouldBeFalse();
        recovery.Notice.ShouldBe(new SessionFaultNotice(DeviceLost, LevelOutcome.Kept, WasPlaying: false, Loss: null));
    }

    [Fact]
    public void A_level_that_could_not_be_taken_is_reported_as_lost()
    {
        SessionRecovery recovery = Running();

        recovery.Died(Start, Crash, wasPlaying: false, () => FromDisk());

        recovery.Notice.ShouldNotBeNull().Level.ShouldBe(LevelOutcome.Lost);
        recovery.Pending.ShouldNotBeNull().Restore.ShouldBeNull();
    }

    [Fact]
    public void What_the_kept_level_could_not_hold_goes_into_the_notice()
    {
        SessionRecovery recovery = Running();

        recovery.Died(Start, DeviceLost, wasPlaying: true, () => Kept("one mesh had no file"));

        SessionFaultNotice notice = recovery.Notice.ShouldNotBeNull();
        notice.Loss.ShouldBe("one mesh had no file");
        notice.WasPlaying.ShouldBeTrue();
    }

    [Fact]
    public void The_notice_is_handed_over_once_when_the_new_session_shows_the_level()
    {
        SessionRecovery recovery = Running();
        recovery.Died(Start, DeviceLost, wasPlaying: false, () => Kept());
        recovery.Launched(recovery.Pending.ShouldNotBeNull());

        recovery.Pending.ShouldBeNull();
        recovery.LevelShown().ShouldNotBeNull().Fault.ShouldBe(DeviceLost);
        recovery.LevelShown().ShouldBeNull();
    }

    [Fact]
    public void A_session_that_never_died_has_nothing_to_report()
    {
        var recovery = new SessionRecovery();
        recovery.Launched(FromDisk());

        recovery.LevelShown().ShouldBeNull();
    }

    [Fact]
    public void A_second_death_soon_after_the_restart_stops_instead_of_restarting()
    {
        SessionRecovery recovery = Running();
        recovery.Died(Start, DeviceLost, wasPlaying: false, () => Kept());
        recovery.Launched(recovery.Pending.ShouldNotBeNull());
        recovery.LevelShown();

        TimeSpan soon = Start + SessionRecovery.RetryWindow - TimeSpan.FromSeconds(1);
        SessionLaunch takenAgain = Kept();

        recovery.Died(soon, DeviceLost, wasPlaying: false, () => takenAgain).ShouldBe(SessionFaultAction.Stop);

        recovery.IsStopped.ShouldBeTrue();
        recovery.Pending.ShouldBeSameAs(takenAgain);
    }

    [Fact]
    public void A_death_long_after_a_restart_gets_a_restart_of_its_own()
    {
        SessionRecovery recovery = Running();
        recovery.Died(Start, DeviceLost, wasPlaying: false, () => Kept());
        recovery.Launched(recovery.Pending.ShouldNotBeNull());
        recovery.LevelShown();

        TimeSpan later = Start + SessionRecovery.RetryWindow;

        recovery.Died(later, Crash, wasPlaying: false, () => Kept()).ShouldBe(SessionFaultAction.Restart);
        recovery.IsStopped.ShouldBeFalse();
    }

    // Its scene is the boot baseplate. Taking that would replace the level
    // the user is waiting for with an empty one.
    [Fact]
    public void A_session_that_dies_before_showing_its_level_is_not_read()
    {
        SessionRecovery recovery = Running();
        SessionLaunch taken = Kept();
        recovery.Died(Start, DeviceLost, wasPlaying: true, () => taken);
        recovery.Launched(taken);

        recovery.Died(Start + TimeSpan.FromSeconds(2), Crash, wasPlaying: false, Never)
            .ShouldBe(SessionFaultAction.Stop);

        recovery.Pending.ShouldBeSameAs(taken);

        // Still the level the first session left, run and all.
        recovery.Notice.ShouldBe(new SessionFaultNotice(Crash, LevelOutcome.Kept, WasPlaying: true, Loss: null));
    }

    [Fact]
    public void A_first_session_that_dies_while_opening_its_level_opens_it_again()
    {
        var recovery = new SessionRecovery();
        SessionLaunch launch = FromDisk();
        recovery.Launched(launch);

        recovery.Died(Start, Crash, wasPlaying: false, Never).ShouldBe(SessionFaultAction.Restart);

        recovery.Pending.ShouldBeSameAs(launch);
        recovery.Notice.ShouldNotBeNull().Level.ShouldBe(LevelOutcome.NotShownYet);
    }

    [Fact]
    public void A_restart_by_hand_gives_back_the_kept_launch_and_drops_the_fault()
    {
        SessionRecovery recovery = StoppedWith(out SessionLaunch held);

        recovery.RestartByHand(Start + TimeSpan.FromMinutes(1)).ShouldBeSameAs(held);

        recovery.IsStopped.ShouldBeFalse();
        recovery.Notice.ShouldBe(new SessionFaultNotice(null, LevelOutcome.Kept, WasPlaying: false, Loss: null));
    }

    [Fact]
    public void A_restart_by_hand_is_refused_while_the_viewport_is_not_stopped()
    {
        Running().RestartByHand(Start).ShouldBeNull();
    }

    [Fact]
    public void A_restart_by_hand_that_dies_at_once_stops_again_with_the_level_still_kept()
    {
        SessionRecovery recovery = StoppedWith(out SessionLaunch held);
        TimeSpan asked = Start + TimeSpan.FromMinutes(5);
        recovery.Launched(recovery.RestartByHand(asked).ShouldNotBeNull());

        recovery.Died(asked + TimeSpan.FromSeconds(1), DeviceLost, wasPlaying: false, Never)
            .ShouldBe(SessionFaultAction.Stop);

        recovery.Pending.ShouldBeSameAs(held);
    }

    [Fact]
    public void A_restart_that_cannot_start_its_session_stops_with_the_level_kept()
    {
        SessionRecovery recovery = Running();
        SessionLaunch taken = Kept();
        recovery.Died(Start, DeviceLost, wasPlaying: false, () => taken);
        recovery.Launched(taken);

        recovery.RestartFailed().ShouldNotBeNull().Level.ShouldBe(LevelOutcome.Kept);

        recovery.IsStopped.ShouldBeTrue();
        recovery.Pending.ShouldBeSameAs(taken);
    }

    [Fact]
    public void A_first_session_that_cannot_start_is_not_a_failed_restart()
    {
        var recovery = new SessionRecovery();
        recovery.Launched(FromDisk());

        recovery.RestartFailed().ShouldBeNull();
        recovery.IsStopped.ShouldBeFalse();
    }

    [Fact]
    public void Closing_the_session_forgets_the_level_and_the_last_restart()
    {
        SessionRecovery recovery = StoppedWith(out _);

        recovery.Reset();

        recovery.IsStopped.ShouldBeFalse();
        recovery.Pending.ShouldBeNull();
        recovery.Notice.ShouldBeNull();

        // A new project gets a restart of its own, however recent the last one.
        recovery.Launched(FromDisk());
        recovery.LevelShown();
        recovery.Died(Start + TimeSpan.FromSeconds(3), DeviceLost, wasPlaying: false, () => Kept())
            .ShouldBe(SessionFaultAction.Restart);
    }

    // Died once, restarted, died again at once: stopped, holding a kept level.
    private static SessionRecovery StoppedWith(out SessionLaunch held)
    {
        SessionRecovery recovery = Running();
        held = Kept();
        SessionLaunch taken = held;

        recovery.Died(Start, DeviceLost, wasPlaying: false, () => taken);
        recovery.Launched(taken);
        recovery.Died(Start + TimeSpan.FromSeconds(1), DeviceLost, wasPlaying: false, Never)
            .ShouldBe(SessionFaultAction.Stop);

        return recovery;
    }
}
