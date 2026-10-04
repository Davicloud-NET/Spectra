using SpectraEngine.Core.Graphics;
using SpectraEngine.Editor.Viewport;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

public sealed class ViewportModePolicyTests
{
    private const string Luid = "9a91010000000000";
    private const string Driver = "31.0.101.5085";

    private static ViewportPreference Proven(ViewportMode mode = ViewportMode.Auto) =>
        new(mode, ViewportModePolicy.RequiredGreenSessions, Luid, Driver);

    private static ViewportCapabilities Capable => ViewportCapabilities.Ideal with
    {
        AdapterLuid = Luid,
        DriverVersion = Driver,
    };

    [Fact]
    public void Every_reason_owes_a_sentence()
    {
        foreach (ViewportChoiceReason reason in Enum.GetValues<ViewportChoiceReason>())
        {
            string sentence = ViewportModePolicy.Describe(reason);

            sentence.ShouldNotBeNullOrWhiteSpace(
                $"{reason} has no sentence, so a viewport chosen for that reason would be chosen silently.");
        }
    }

    [Fact]
    public void Every_fallback_the_decision_can_reach_is_exercised_here()
    {
        var reached = Cases().Select(c => c.Expected).ToHashSet();

        // Not a decision: a live composited session reports it mid-run.
        reached.Add(ViewportChoiceReason.FirstUpdateFaulted);

        var all = Enum.GetValues<ViewportChoiceReason>().ToHashSet();

        all.Except(reached).ShouldBeEmpty(
            "a reason with no case is a fallback nobody has proved the policy can explain.");
    }

    [Theory]
    [MemberData(nameof(CaseData))]
    public void The_decision_names_its_reason_and_never_falls_back_without_one(string name)
    {
        Case scenario = Cases().Single(c => c.Name == name);

        ViewportDecision decision = ViewportModePolicy.Decide(
            scenario.Settings, scenario.Capabilities, scenario.Backend);

        decision.Reason.ShouldBe(scenario.Expected);
        decision.UseComposition.ShouldBe(scenario.UseComposition);

        decision.Explanation.ShouldNotBeNullOrWhiteSpace();
    }

    private sealed record Case(
        string Name,
        ViewportPreference Settings,
        ViewportCapabilities Capabilities,
        GraphicsBackend Backend,
        bool UseComposition,
        ViewportChoiceReason Expected);

    public static TheoryData<string> CaseData()
    {
        var data = new TheoryData<string>();
        foreach (Case scenario in Cases())
            data.Add(scenario.Name);

        return data;
    }

    private static IReadOnlyList<Case> Cases() =>
    [
        new("explicit composition",
            new ViewportPreference(ViewportMode.Composition, 0, string.Empty, string.Empty),
            Capable, GraphicsBackend.D3D11, true, ViewportChoiceReason.ExplicitComposition),

        new("auto, proven",
            Proven(), Capable, GraphicsBackend.D3D11, true, ViewportChoiceReason.ProvenByHistory),

        new("explicit native",
            Proven(ViewportMode.Native),
            Capable, GraphicsBackend.D3D11, false, ViewportChoiceReason.ExplicitNative),

        new("auto, nothing recorded",
            ViewportPreference.Default,
            Capable, GraphicsBackend.D3D11, false, ViewportChoiceReason.NotYetProven),

        new("auto, another adapter",
            Proven() with { AdapterLuid = "0000000000000000" },
            Capable, GraphicsBackend.D3D11, false, ViewportChoiceReason.AdapterChanged),

        new("auto, another driver",
            Proven() with { DriverVersion = "30.0.100.1000" },
            Capable, GraphicsBackend.D3D11, false, ViewportChoiceReason.DriverChanged),

        new("composition on opengl",
            new ViewportPreference(ViewportMode.Composition, 0, string.Empty, string.Empty),
            Capable, GraphicsBackend.OpenGL, false, ViewportChoiceReason.BackendIsOpenGl),

        new("no compositor",
            new ViewportPreference(ViewportMode.Composition, 0, string.Empty, string.Empty),
            Capable with { HasCompositor = false },
            GraphicsBackend.D3D11, false, ViewportChoiceReason.NoCompositor),

        new("no gpu interop",
            new ViewportPreference(ViewportMode.Composition, 0, string.Empty, string.Empty),
            Capable with { HasGpuInterop = false },
            GraphicsBackend.D3D11, false, ViewportChoiceReason.NoGpuInterop),

        new("handle kind unsupported",
            new ViewportPreference(ViewportMode.Composition, 0, string.Empty, string.Empty),
            Capable with { SupportsD3D11NtHandle = false },
            GraphicsBackend.D3D11, false, ViewportChoiceReason.HandleKindUnsupported),

        new("no keyed mutex",
            new ViewportPreference(ViewportMode.Composition, 0, string.Empty, string.Empty),
            Capable with { SupportsKeyedMutex = false },
            GraphicsBackend.D3D11, false, ViewportChoiceReason.NoKeyedMutexSync),

        new("dry run refused",
            new ViewportPreference(ViewportMode.Composition, 0, string.Empty, string.Empty),
            Capable with { DryRunImported = false },
            GraphicsBackend.D3D11, false, ViewportChoiceReason.DryRunImportFailed),
    ];

    [Fact]
    public void Five_green_sessions_are_required_and_four_are_not()
    {
        ViewportPreference four = Proven() with
        {
            GreenSessions = ViewportModePolicy.RequiredGreenSessions - 1,
        };

        ViewportModePolicy.Decide(four, Capable, GraphicsBackend.D3D11)
            .Reason.ShouldBe(ViewportChoiceReason.NotYetProven);

        ViewportModePolicy.Decide(Proven(), Capable, GraphicsBackend.D3D11)
            .UseComposition.ShouldBeTrue();
    }

    [Fact]
    public void An_explicit_native_choice_beats_a_green_history()
    {
        ViewportDecision decision = ViewportModePolicy.Decide(
            Proven(ViewportMode.Native), Capable, GraphicsBackend.D3D11);

        decision.UseComposition.ShouldBeFalse();
        decision.Reason.ShouldBe(ViewportChoiceReason.ExplicitNative);
    }

    [Fact]
    public void A_driver_change_resets_the_count()
    {
        ViewportPreference rebased = ViewportModePolicy.Rebase(Proven(), Luid, "31.0.101.9999");

        rebased.GreenSessions.ShouldBe(0);
        rebased.DriverVersion.ShouldBe("31.0.101.9999");
    }

    [Fact]
    public void An_adapter_change_resets_the_count()
    {
        ViewportPreference rebased = ViewportModePolicy.Rebase(Proven(), "1111111111111111", Driver);

        rebased.GreenSessions.ShouldBe(0);
        rebased.AdapterLuid.ShouldBe("1111111111111111");
    }

    [Fact]
    public void The_same_machine_keeps_its_count()
    {
        ViewportModePolicy.Rebase(Proven(), Luid, Driver)
            .GreenSessions.ShouldBe(ViewportModePolicy.RequiredGreenSessions);
    }

    [Fact]
    public void A_green_session_lengthens_the_run_and_a_red_one_ends_it()
    {
        var settings = new ViewportPreference(ViewportMode.Auto, 2, Luid, Driver);

        ViewportModePolicy.Record(settings, sessionGreen: true).GreenSessions.ShouldBe(3);
        ViewportModePolicy.Record(settings, sessionGreen: false).GreenSessions.ShouldBe(0);
    }

    [Fact]
    public void The_run_is_capped_at_what_the_question_needs()
    {
        ViewportPreference settings = Proven();

        ViewportModePolicy.Record(settings, sessionGreen: true)
            .GreenSessions.ShouldBe(ViewportModePolicy.RequiredGreenSessions);
    }

    [Theory]
    [InlineData(0, false, true, true)]
    [InlineData(1, false, true, false)]
    [InlineData(0, true, true, false)]
    [InlineData(0, false, false, false)]
    public void A_session_is_green_only_when_all_three_conditions_hold(
        int debugLayerErrors, bool faulted, bool compareGreen, bool expected)
    {
        // compareGreen: a double sRGB encode raises no error and nothing on
        // the debug layer, so the other two cannot see it.
        ViewportModePolicy.IsSessionGreen(debugLayerErrors, faulted, compareGreen).ShouldBe(expected);
    }

    [Fact]
    public void A_decision_that_the_machine_cannot_change_is_taken_without_measuring_it()
    {
        // Measuring opens a graphics device, too costly for every launch.
        ViewportModePolicy.RequiresMeasurement(Proven(ViewportMode.Native), GraphicsBackend.D3D11)
            .ShouldBeFalse();

        ViewportModePolicy.RequiresMeasurement(Proven(), GraphicsBackend.OpenGL)
            .ShouldBeFalse();

        ViewportModePolicy.RequiresMeasurement(ViewportPreference.Default, GraphicsBackend.D3D11)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_decision_that_turns_on_the_machine_measures_it()
    {
        ViewportModePolicy.RequiresMeasurement(
            new ViewportPreference(ViewportMode.Composition, 0, string.Empty, string.Empty),
            GraphicsBackend.D3D11).ShouldBeTrue();

        ViewportModePolicy.RequiresMeasurement(Proven(), GraphicsBackend.D3D11).ShouldBeTrue();
    }

    [Fact]
    public void An_unmeasured_launch_never_reaches_composition()
    {
        ViewportDecision decision = ViewportModePolicy.Decide(
            new ViewportPreference(ViewportMode.Composition, 0, string.Empty, string.Empty),
            ViewportCapabilities.NotMeasured,
            GraphicsBackend.D3D11);

        decision.UseComposition.ShouldBeFalse();
        decision.Reason.ShouldBe(ViewportChoiceReason.NoCompositor);
    }

    [Theory]
    [InlineData("--viewport=native", ViewportMode.Native)]
    [InlineData("--viewport=composition", ViewportMode.Composition)]
    [InlineData("--viewport=composited", ViewportMode.Composition)]
    [InlineData("--viewport=auto", ViewportMode.Auto)]
    [InlineData("--VIEWPORT=Native", ViewportMode.Native)]
    public void The_switch_names_a_mode(string arg, ViewportMode expected) =>
        ViewportModePolicy.RequestedMode(["d3d11", arg]).ShouldBe(expected);

    [Fact]
    public void A_command_line_that_says_nothing_leaves_the_setting_alone()
    {
        // Null, not Auto: Auto would overwrite a stored preference.
        ViewportModePolicy.RequestedMode(["d3d11", "--play"]).ShouldBeNull();
        ViewportModePolicy.RequestedMode(["--viewport=sideways"]).ShouldBeNull();
    }

    [Fact]
    public void The_last_spelling_of_the_switch_wins()
    {
        ViewportModePolicy.RequestedMode(["--viewport=composition", "--viewport=native"])
            .ShouldBe(ViewportMode.Native);
    }

    [Fact]
    public void Every_mode_has_a_word_that_parses_back()
    {
        foreach (ViewportMode mode in Enum.GetValues<ViewportMode>())
        {
            string word = ViewportModePolicy.NameOf(mode);

            // Both tables are hand-written (no enum reflection under AOT).
            ViewportModePolicy.TryParseMode(word, out ViewportMode parsed).ShouldBeTrue();
            parsed.ShouldBe(mode);
        }
    }
}
