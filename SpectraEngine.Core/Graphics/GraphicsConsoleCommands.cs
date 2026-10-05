using SpectraEngine.Core.ConsoleSystem;
using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>The console commands that reach the renderer.</summary>
public static class GraphicsConsoleCommands
{
    /// <summary>The command that fakes a lost graphics device.</summary>
    public const string FakeDeviceLoss = "fake_device_loss";

    /// <summary>Adds the commands to <paramref name="table"/>.</summary>
    public static void Register(ConCommandTable table, Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(renderer);

        table.Add(new ConCommand(
            FakeDeviceLoss,
            FakeDeviceLoss,
            "Ends the next frame as a lost graphics device does, to test what happens then. " +
            "The demo ends with an error. The editor restarts its viewport.",
            (in ConArgs args) => LoseDevice(renderer, in args)));
    }

    private static void LoseDevice(Renderer renderer, in ConArgs args)
    {
        if (!renderer.CanLoseDevice)
        {
            args.Out.Error($"{FakeDeviceLoss}: the {renderer.Backend} renderer has no device to lose.");
            return;
        }

        renderer.SimulateDeviceLoss();
        args.Out.Print("The graphics device is reported lost at the end of this frame.");
    }
}
