using SpectraEngine.Core.Entities;
using System;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// One input that was delivered, refused or lost while a level ran, as names
/// and ids. The strings are empty where they do not apply.
/// </summary>
public readonly record struct LogicEventInfo
{
    /// <summary>Counts up by one per event, so a reader can tell which it has seen.</summary>
    public long Number { get; init; }

    /// <summary>The tick it happened on.</summary>
    public long Tick { get; init; }

    /// <summary>Delivered, refused, or the target was missing.</summary>
    public EntityTraceKind Kind { get; init; }

    /// <summary>The sending entity's node, or empty when no output sent the input.</summary>
    public Guid SourceId { get; init; }

    /// <summary>The sending entity's name.</summary>
    public string SourceName { get; init; }

    /// <summary>The output that fired.</summary>
    public string Output { get; init; }

    /// <summary>The wire's index in the sender's connection list, or -1.</summary>
    public int Wire { get; init; }

    /// <summary>The node of the entity reached, or empty when the target was missing.</summary>
    public Guid TargetId { get; init; }

    /// <summary>The name of the entity reached, or the target as the wire spells it.</summary>
    public string TargetName { get; init; }

    /// <summary>The input's name.</summary>
    public string Input { get; init; }

    /// <summary>The argument sent with it.</summary>
    public string Parameter { get; init; }
}
