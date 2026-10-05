using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The console command that fakes a lost graphics device.</summary>
public sealed class GraphicsConsoleCommandTests
{
    private static (SpectraConsole Console, FakeRenderer Renderer) Rig(bool hasDevice)
    {
        var renderer = new FakeRenderer { HasDevice = hasDevice };
        var console = new SpectraConsole();
        GraphicsConsoleCommands.Register(console.Commands, renderer);
        return (console, renderer);
    }

    [Fact]
    public void The_command_asks_the_renderer_to_lose_its_device_at_the_next_present()
    {
        (SpectraConsole console, FakeRenderer renderer) = Rig(hasDevice: true);

        console.Execute(GraphicsConsoleCommands.FakeDeviceLoss, default);

        console.Output.Drain().ShouldHaveSingleItem().Severity.ShouldBe(LogLevel.Information);
        renderer.TakeDeviceLoss().ShouldBeTrue();
    }

    [Fact]
    public void One_request_is_one_loss()
    {
        (SpectraConsole console, FakeRenderer renderer) = Rig(hasDevice: true);
        console.Execute(GraphicsConsoleCommands.FakeDeviceLoss, default);

        renderer.TakeDeviceLoss().ShouldBeTrue();
        renderer.TakeDeviceLoss().ShouldBeFalse();
    }

    [Fact]
    public void A_renderer_with_no_device_refuses_and_says_why()
    {
        (SpectraConsole console, FakeRenderer renderer) = Rig(hasDevice: false);

        console.Execute(GraphicsConsoleCommands.FakeDeviceLoss, default);

        ConsoleLine line = console.Output.Drain().ShouldHaveSingleItem();
        line.Severity.ShouldBe(LogLevel.Error);
        line.Text.ShouldContain("no device to lose");
        renderer.TakeDeviceLoss().ShouldBeFalse();
    }

    [Fact]
    public void Help_lists_the_command()
    {
        (SpectraConsole console, _) = Rig(hasDevice: true);

        console.Execute("help", default);

        console.Output.Drain().ShouldContain(line => line.Text.StartsWith(GraphicsConsoleCommands.FakeDeviceLoss));
    }
}
