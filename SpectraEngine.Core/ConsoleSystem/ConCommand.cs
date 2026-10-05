using System;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>A console command: the name it is typed by, how to use it and the code it runs.</summary>
public sealed class ConCommand
{
    /// <param name="name">What the user types. Case does not matter.</param>
    /// <param name="usage">The name with its arguments, such as <c>help [command]</c>.</param>
    /// <param name="help">One line on what the command does.</param>
    /// <param name="handler">The code to run.</param>
    public ConCommand(string name, string usage, string help, ConCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(help);
        ArgumentNullException.ThrowIfNull(handler);

        Name = name;
        Usage = usage;
        Help = help;
        Handler = handler;
    }

    /// <summary>What the user types.</summary>
    public string Name { get; }

    /// <summary>The name with its arguments.</summary>
    public string Usage { get; }

    /// <summary>One line on what the command does.</summary>
    public string Help { get; }

    /// <summary>The code to run.</summary>
    public ConCommandHandler Handler { get; }
}
