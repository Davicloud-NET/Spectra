using System.Collections.Generic;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// What the scripts of a running level are connected to, for a view that
/// draws it beside the wiring. Made by code, so it lasts no longer than the
/// level runs. Holds copies, never live lists.
/// </summary>
public sealed class LogicScriptWiring
{
    /// <summary>How many wires one capture will list.</summary>
    public const int MaxWires = 2000;

    /// <summary>No script is connected to anything.</summary>
    public static LogicScriptWiring None { get; } = new();

    /// <summary>
    /// The outputs scripts hear right now, and the inputs they have sent
    /// since a view began to watch. One for each script, entity, port and
    /// line of code, in an order that does not change while they are the same.
    /// </summary>
    public IReadOnlyList<LogicScriptWire> Wires { get; init; } = [];

    /// <summary>Whether the level has more of them than <see cref="Wires"/> carries.</summary>
    public bool IsTruncated { get; init; }
}
