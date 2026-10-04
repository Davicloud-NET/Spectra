using SpectraEngine.Core.Graphics;
using SpectraEngine.Executable;
using System;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The demo host's startup switches and their defaults. The self-test moves a
/// real brush, so it must stay off unless asked for.
/// </summary>
public sealed class DemoStartupOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    public void Extended_gbuffer_is_explicit_and_preserved_with_environment_selftest(string? environment)
    {
        DemoStartupOptions.Parse(["d3d12"], environment).GBufferLayout.ShouldBe(GBufferLayout.Standard);
        DemoStartupOptions.Parse(["d3d12", "--gbuffer=extended"], environment).GBufferLayout.ShouldBe(GBufferLayout.Extended);
        Should.Throw<ArgumentException>(() => DemoStartupOptions.Parse(["--gbuffer=unknown"], environment));
    }
    [Fact]
    public void Performance_defaults_leave_animation_and_presentation_unchanged()
    {
        var options = DemoStartupOptions.Parse([], null);
        options.DemoCsgAnimation.ShouldBeFalse();
        options.Uncapped.ShouldBeFalse();
        options.FrameContexts.ShouldBe(2);
        var selected = DemoStartupOptions.Parse(["--demo-animation=csg", "--uncapped", "--frame-contexts=3"], null);
        selected.DemoCsgAnimation.ShouldBeTrue();
        selected.Uncapped.ShouldBeTrue();
        selected.FrameContexts.ShouldBe(3);
        selected.SelfTestEnabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("--demo-animation")]
    [InlineData("--demo-animation=yes")]
    [InlineData("--frame-contexts=0")]
    [InlineData("--frame-contexts=4")]
    public void Invalid_performance_settings_are_reported(string argument) =>
        Should.Throw<ArgumentException>(() => DemoStartupOptions.Parse([argument], null));

    [Fact]
    public void The_self_test_is_off_when_nothing_asks_for_it()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([], selfTestEnvironmentValue: null);

        options.SelfTestEnabled.ShouldBeFalse();
        options.SelfTestSource.ShouldBe(SelfTestSource.Default);
    }

    [Theory]
    [InlineData("opengl")]
    [InlineData("d3d11")]
    [InlineData("d3d12")]
    public void The_self_test_is_off_for_a_plain_backend_run(string backend)
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([backend], selfTestEnvironmentValue: null);

        options.SelfTestEnabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("--selftest")]
    [InlineData("-selftest")]
    [InlineData("/selftest")]
    [InlineData("--self-test")]
    [InlineData("--selftest=true")]
    [InlineData("--selftest=1")]
    [InlineData("--selftest=on")]
    public void The_switch_turns_the_self_test_on(string argument)
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([argument], selfTestEnvironmentValue: null);

        options.SelfTestEnabled.ShouldBeTrue();
        options.SelfTestSource.ShouldBe(SelfTestSource.CommandLine);
    }

    [Fact]
    public void The_switch_composes_with_a_backend_in_either_order()
    {
        DemoStartupOptions backendFirst = DemoStartupOptions.Parse(["d3d11", "--selftest"], null);
        DemoStartupOptions switchFirst = DemoStartupOptions.Parse(["--selftest", "d3d11"], null);

        backendFirst.ShouldBe(switchFirst);
        backendFirst.Backend.ShouldBe(GraphicsBackend.D3D11);
        backendFirst.SelfTestEnabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("ON")]
    public void The_environment_variable_turns_the_self_test_on(string value)
    {
        DemoStartupOptions options = DemoStartupOptions.Parse([], value);

        options.SelfTestEnabled.ShouldBeTrue();
        options.SelfTestSource.ShouldBe(SelfTestSource.Environment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("off")]
    public void The_environment_variable_leaves_the_self_test_off(string? value)
    {
        DemoStartupOptions.Parse([], value).SelfTestEnabled.ShouldBeFalse();
    }

    [Fact]
    public void An_explicit_command_line_no_overrides_an_inherited_environment_yes()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["--selftest=false"], "true");

        options.SelfTestEnabled.ShouldBeFalse();
        options.SelfTestSource.ShouldBe(SelfTestSource.CommandLine);
    }

    [Fact]
    public void An_unparsable_environment_value_is_a_usage_error()
    {
        // Ignoring it would leave a gate script thinking the self-test ran.
        Should.Throw<ArgumentException>(() => DemoStartupOptions.Parse([], "maybe"));
    }

    [Theory]
    [InlineData("--selftst")]
    [InlineData("--selftest=maybe")]
    [InlineData("nonsense")]
    public void A_misspelled_argument_is_a_usage_error(string argument)
    {
        Should.Throw<ArgumentException>(() => DemoStartupOptions.Parse([argument], null));
    }

    [Theory]
    [InlineData("opengl", GraphicsBackend.OpenGL)]
    [InlineData("gl", GraphicsBackend.OpenGL)]
    [InlineData("--d3d11", GraphicsBackend.D3D11)]
    [InlineData("dx11", GraphicsBackend.D3D11)]
    [InlineData("d3d12", GraphicsBackend.D3D12)]
    [InlineData("backend=d3d12", GraphicsBackend.D3D12)]
    [InlineData("--backend=vulkan", GraphicsBackend.Vulkan)]
    public void Backend_spellings_still_parse_as_they_did(string argument, GraphicsBackend expected)
    {
        DemoStartupOptions.Parse([argument], null).Backend.ShouldBe(expected);
    }

    [Fact]
    public void Booting_from_packs_is_off_unless_a_project_is_named_with_it()
    {
        // --pack alone would run the demo scene off loose files and look like a
        // passing cooked run.
        DemoStartupOptions.Parse(["d3d11", "--project=Game"], null).BootFromPacks.ShouldBeFalse();

        Should.Throw<ArgumentException>(() => DemoStartupOptions.Parse(["--pack"], null))
            .Message.ShouldContain("--project");
    }

    [Fact]
    public void The_dev_overlay_only_means_something_over_a_pack_mount()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(
            ["d3d11", "--project=Game", "--pack", "--dev"], null);

        options.BootFromPacks.ShouldBeTrue();
        options.DevContentOverlay.ShouldBeTrue();

        Should.Throw<ArgumentException>(
            () => DemoStartupOptions.Parse(["--project=Game", "--dev"], null))
            .Message.ShouldContain("--pack");
    }

    [Fact]
    public void The_pack_switches_survive_the_self_test_environment_path()
    {
        // Parse builds the record at three separate exits. This covers the
        // environment one.
        DemoStartupOptions options = DemoStartupOptions.Parse(
            ["d3d11", "--project=Game", "--pack"], "true");

        options.SelfTestEnabled.ShouldBeTrue();
        options.SelfTestSource.ShouldBe(SelfTestSource.Environment);
        options.BootFromPacks.ShouldBeTrue();
        options.ProjectPath.ShouldBe("Game");
    }

    [Fact]
    public void The_backend_defaults_to_opengl()
    {
        DemoStartupOptions.Parse([], null).Backend.ShouldBe(GraphicsBackend.OpenGL);
    }

    [Fact]
    public void The_fullscreen_cycle_is_off_when_nothing_asks_for_it()
    {
        DemoStartupOptions.Parse(["d3d12"], null).FullscreenCycleInterval.ShouldBeNull();
    }

    [Theory]
    [InlineData("--fullscreen-cycle")]
    [InlineData("fullscreen-cycle")]
    [InlineData("--fullscreencycle")]
    public void A_bare_fullscreen_cycle_switch_uses_the_harness_default(string argument)
    {
        DemoStartupOptions.Parse([argument], null).FullscreenCycleInterval
            .ShouldBe(TimeSpan.FromSeconds(FullscreenCycleHarness.DefaultIntervalSeconds));
    }

    [Fact]
    public void The_fullscreen_cycle_interval_is_read_invariantly()
    {
        // Gate scripts write "0.5". A comma-decimal culture would reject it.
        DemoStartupOptions.Parse(["--fullscreen-cycle=0.5"], null).FullscreenCycleInterval
            .ShouldBe(TimeSpan.FromSeconds(0.5));
    }

    [Theory]
    [InlineData("--fullscreen-cycle=0")]
    [InlineData("--fullscreen-cycle=-2")]
    [InlineData("--fullscreen-cycle=soon")]
    public void A_non_positive_or_unparsable_interval_is_a_usage_error(string argument)
    {
        Should.Throw<ArgumentException>(() => DemoStartupOptions.Parse([argument], null));
    }

    [Fact]
    public void The_fullscreen_cycle_survives_the_self_test_environment_path()
    {
        // Parse builds the record at three separate exits. This covers the
        // environment one.
        DemoStartupOptions options = DemoStartupOptions.Parse(["d3d11", "--fullscreen-cycle=1"], "true");

        options.SelfTestEnabled.ShouldBeTrue();
        options.SelfTestSource.ShouldBe(SelfTestSource.Environment);
        options.Backend.ShouldBe(GraphicsBackend.D3D11);
        options.FullscreenCycleInterval.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void The_pacing_probe_is_off_unless_it_is_asked_for()
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(["d3d11"], selfTestEnvironmentValue: null);

        options.PacingProbe.ShouldBeFalse();
    }

    [Theory]
    [InlineData("--pacing-probe")]
    [InlineData("--pacingprobe")]
    [InlineData("--pacing-probe=true")]
    public void The_switch_turns_the_pacing_probe_on(string argument)
    {
        DemoStartupOptions options = DemoStartupOptions.Parse(
            ["d3d11", argument], selfTestEnvironmentValue: null);

        options.PacingProbe.ShouldBeTrue();
    }

    [Fact]
    public void The_pacing_probe_refuses_opengl_by_name()
    {
        // OpenGL has no shared target. It is also the default backend, so naming
        // none is the same case.
        Should.Throw<ArgumentException>(
            () => DemoStartupOptions.Parse(["opengl", "--pacing-probe"], selfTestEnvironmentValue: null));
        Should.Throw<ArgumentException>(
            () => DemoStartupOptions.Parse(["--pacing-probe"], selfTestEnvironmentValue: null));
    }
}
