using System;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// One thing a running script does with an entity from one line of code:
/// it hears an output, or it has sent an input.
/// </summary>
public readonly record struct LogicScriptWire
{
    /// <summary>The script's node. A module's code counts for the script that asked for it.</summary>
    public Guid Script { get; init; }

    /// <summary>The entity's node.</summary>
    public Guid Entity { get; init; }

    /// <summary>The input the script sent, or the output it hears.</summary>
    public string Port { get; init; }

    /// <summary>Whether the script sends <see cref="Port"/>. False when it hears it.</summary>
    public bool IsSend { get; init; }

    /// <summary>The file of the line that connected or sent, as a content path. Empty when it is not known.</summary>
    public string File { get; init; }

    /// <summary>The line in <see cref="File"/>, or 0 when it is not known.</summary>
    public int Line { get; init; }
}
