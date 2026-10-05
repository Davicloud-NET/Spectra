using System;

namespace SpectraEngine.Core.Inspection;

/// <summary>What one wire has done since the level started.</summary>
public readonly record struct LogicWireActivity
{
    /// <summary>The sending entity's node.</summary>
    public Guid NodeId { get; init; }

    /// <summary>The wire's index in that entity's connection list.</summary>
    public int Wire { get; init; }

    /// <summary>How many times its output fired it.</summary>
    public int Fired { get; init; }

    /// <summary>The tick it last fired on.</summary>
    public long LastFiredTick { get; init; }

    /// <summary>How many times its target matched nothing.</summary>
    public int Missed { get; init; }

    /// <summary>How many times an entity it reached had no such input.</summary>
    public int Refused { get; init; }

    /// <summary>How many inputs it has queued that are not due yet.</summary>
    public int Waiting { get; init; }

    /// <summary>The world time the soonest of those was queued at. Zero with none waiting.</summary>
    public float WaitingSince { get; init; }

    /// <summary>The world time the soonest of those is due at. Zero with none waiting.</summary>
    public float WaitingDue { get; init; }
}
