using System.Text;

namespace SpectraEngine.Core.ConsoleSystem;

// The commands every console has.
internal static class BuiltinConsoleCommands
{
    public static void Register(ConCommandTable table)
    {
        table.Add(new ConCommand("echo", "echo <text>", "Prints its arguments.", Echo));
        table.Add(new ConCommand(
            "help",
            "help [command]",
            "Lists the commands, or says how to use one.",
            (in ConArgs args) => Help(table, in args)));
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
}
