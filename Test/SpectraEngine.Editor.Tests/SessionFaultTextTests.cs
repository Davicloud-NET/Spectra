using SpectraEngine.Core;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>What the shell says after an engine session died.</summary>
public sealed class SessionFaultTextTests
{
    private const string Logs = @"C:\Spectra\logs";

    private static readonly EngineFault DeviceLost = new("DXGI_ERROR_DEVICE_REMOVED (0x887A0005)", IsDeviceLoss: true);
    private static readonly EngineFault Crash = new("NullReferenceException in Scene.Update", IsDeviceLoss: false);

    private static SessionFaultNotice Notice(
        EngineFault? fault, LevelOutcome level = LevelOutcome.Kept, bool wasPlaying = false, string? loss = null) =>
        new(fault, level, wasPlaying, loss);

    [Fact]
    public void A_lost_device_is_said_in_plain_words()
    {
        SessionFaultText.Restarted(Notice(DeviceLost), hasSavedLevel: true, Logs).ShouldBe(
            "The graphics device was lost, so the viewport was restarted. The level is as you left it. " +
            "Undo history and the selection did not survive.");
    }

    [Fact]
    public void Any_other_fault_points_at_the_log()
    {
        SessionFaultText.Restarted(Notice(Crash), hasSavedLevel: true, Logs).ShouldBe(
            "The engine stopped on an error, so the viewport was restarted. The level is as you left it. " +
            @"Undo history and the selection did not survive. The log in C:\Spectra\logs has the details.");
    }

    [Fact]
    public void The_exception_text_stays_in_the_log()
    {
        string device = SessionFaultText.Restarted(Notice(DeviceLost), hasSavedLevel: true, Logs);
        string crash = SessionFaultText.Stopped(Notice(Crash), Logs);

        device.ShouldNotContain("DXGI");
        device.ShouldNotContain("0x");
        crash.ShouldNotContain("NullReferenceException");
    }

    [Fact]
    public void A_run_that_was_stopped_is_mentioned()
    {
        SessionFaultText.Restarted(Notice(DeviceLost, wasPlaying: true), hasSavedLevel: true, Logs)
            .ShouldEndWith("did not survive. The run was stopped.");
    }

    [Fact]
    public void What_did_not_come_back_is_named()
    {
        SessionFaultText.Restarted(Notice(DeviceLost, loss: "1 mesh node(s) had no file"), hasSavedLevel: true, Logs)
            .ShouldEndWith("Not everything came back: 1 mesh node(s) had no file.");
    }

    [Fact]
    public void A_lost_level_with_a_save_says_it_was_opened_from_that_save()
    {
        SessionFaultText.Restarted(Notice(DeviceLost, LevelOutcome.Lost), hasSavedLevel: true, Logs).ShouldBe(
            "The graphics device was lost, so the viewport was restarted. The level could not be kept, " +
            "so it was opened again from its last save. Changes since then are lost.");
    }

    [Fact]
    public void A_lost_level_with_no_save_says_this_is_a_new_one()
    {
        SessionFaultText.Restarted(Notice(DeviceLost, LevelOutcome.Lost), hasSavedLevel: false, Logs).ShouldBe(
            "The graphics device was lost, so the viewport was restarted. The level could not be kept, " +
            "so this is a new one.");
    }

    [Fact]
    public void A_level_that_was_still_opening_claims_nothing_about_edits()
    {
        SessionFaultText.Restarted(Notice(DeviceLost, LevelOutcome.NotShownYet), hasSavedLevel: true, Logs)
            .ShouldBe("The graphics device was lost, so the viewport was restarted.");
    }

    [Fact]
    public void A_restart_by_hand_names_no_fault()
    {
        SessionFaultText.Restarted(Notice(null), hasSavedLevel: true, Logs).ShouldBe(
            "The viewport was restarted. The level is as you left it. " +
            "Undo history and the selection did not survive.");
    }

    // The status line trims, so the way out has to come before the rest.
    [Fact]
    public void A_stopped_viewport_leads_with_the_way_to_restart_it()
    {
        SessionFaultText.Stopped(Notice(DeviceLost), Logs).ShouldBe(
            "The graphics device was lost again right after the viewport restarted, so it was left stopped. " +
            "Type restart in the console to try again. The level is kept.");

        SessionFaultText.Stopped(Notice(Crash, LevelOutcome.Lost), Logs).ShouldBe(
            "The engine stopped on an error again right after the viewport restarted, so it was left stopped. " +
            @"Type restart in the console to try again. The level could not be kept. " +
            @"The log in C:\Spectra\logs has the details.");
    }

    [Fact]
    public void A_restart_that_could_not_start_says_why_and_how_to_try_again()
    {
        SessionFaultText.StartFailed(Notice(DeviceLost), "No adapter was found.").ShouldBe(
            "The viewport could not be started again: No adapter was found. " +
            "Type restart in the console to try again. The level is kept.");
    }

    [Fact]
    public void The_console_verb_in_the_messages_is_the_one_the_console_knows()
    {
        ConsoleCommands.Names.ShouldContain(SessionFaultText.RestartVerb);
        SessionFaultText.SaveWhileStopped.ShouldContain($"Type {SessionFaultText.RestartVerb} ");
    }
}
