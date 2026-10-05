using System.Globalization;
using System.Text;

namespace SpectraEngine.Core.ConsoleSystem;

// The commands every console has.
internal static class BuiltinConsoleCommands
{
    public static void Register(SpectraConsole console)
    {
        ConCommandTable table = console.Commands;

        table.Add(new ConCommand("echo", "echo <text>", "Prints its arguments.", Echo));
        table.Add(new ConCommand(
            "help",
            "help [command]",
            "Lists the commands, or says how to use one.",
            (in ConArgs args) => Help(table, in args)));
        table.Add(new ConCommand(
            "wait",
            "wait [frames]",
            "Runs what follows a frame later, or as many frames later as it is given.",
            (in ConArgs args) => Wait(console, in args)));
    }

    private static void Echo(in ConArgs args)
    {
        var text = new StringBuilder();
        for (int i = 0; i < args.Count; i++)
        {
            if (i > 0)
                text.Append(' ');
            text.Append(args[i]);
        }

        args.Out.Print(text.ToString());
    }

    private static void Help(ConCommandTable table, in ConArgs args)
    {
        if (args.Count == 0)
        {
            foreach (ConCommand command in table.Commands)
                args.Out.Print($"{command.Name}  {command.Help}");
            return;
        }

        if (!table.TryGet(args[0], out ConCommand? named))
        {
            args.Out.Error($"help: no command named '{args[0]}'.");
            return;
        }

        args.Out.Print(named.Usage);
        args.Out.Print(named.Help);
    }

    private static void Wait(SpectraConsole console, in ConArgs args)
    {
        int frames = 1;
        if (args.Count > 0
            && (!int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out frames) || frames < 1))
        {
            // No wait at all: a typo must not turn into a pause nobody asked for.
            args.Out.Error($"wait: '{args[0]}' is not a whole number of frames, 1 or more.");
            return;
        }

        if (frames > SpectraConsole.MaxWaitFrames)
        {
            args.Out.Warn(
                $"wait: {frames} frames is over the limit, so it waits {SpectraConsole.MaxWaitFrames}.");
            frames = SpectraConsole.MaxWaitFrames;
        }

        console.HoldFor(frames);
    }
}
