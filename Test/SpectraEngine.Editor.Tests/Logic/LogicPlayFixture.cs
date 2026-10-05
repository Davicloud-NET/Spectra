using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Inspection;
using System;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

// What the engine says about the vault level a moment after both buttons
// were pressed: two wires fired, two firing, one waiting and one missed.
internal static class LogicPlayFixture
{
    public const long Tick = 812;
    public const float Time = 13.5f;

    public static FrameSnapshot Snapshot(LogicGraphInfo? level, LogicPlayInfo? play = null, params Guid[] selected) =>
        new() { LogicGraph = level, LogicPlay = play, SelectedIds = selected, IsPlaying = play is not null };

    public static LogicWireActivity Fired(Guid node, int wire, int times, long lastTick) =>
        new() { NodeId = node, Wire = wire, Fired = times, LastFiredTick = lastTick };

    public static LogicPlayInfo Playing(params LogicWireActivity[] wires) =>
        new() { Tick = Tick, Time = Time, Wires = wires };

    public static LogicPlayInfo VaultPlaying() => new()
    {
        Tick = Tick,
        Time = Time,
        Wires =
        [
            Fired(ButtonA, 0, 1, 700),
            Fired(ButtonB, 0, 1, 760),
            Fired(Presses, 0, 1, 811),
            Fired(OpenVault, 0, 1, 812),
            new()
            {
                NodeId = OpenVault,
                Wire = 1,
                Fired = 1,
                LastFiredTick = 812,
                Waiting = 1,
                WaitingSince = 12.9f,
                WaitingDue = 14.9f,
            },
            new() { NodeId = OpenVault, Wire = 2, Fired = 1, LastFiredTick = 812, Missed = 1 },
        ],
        States =
        [
            new(ButtonA, "state", "in"),
            new(ButtonB, "state", "in"),
            new(Presses, "value", "2 of 2"),
            new(OpenVault, "fired", "1 time"),
            new(VaultDoor, "opening", "14 of 39 ticks"),
            new(Lift, "at", "0 of 88 ticks"),
            new(LiftButton, "state", "out"),
            new(StartZone, "touched", "no"),
            new(StartDoor, "state", "closed"),
        ],
        Recent =
        [
            Event(1, EntityTraceKind.InputDelivered, "Presses", "OnHitMax", "OpenVault", "Trigger"),
            Event(2, EntityTraceKind.InputDelivered, "OpenVault", "OnTrigger", "VaultDoor", "Open"),
            Event(3, EntityTraceKind.TargetMissing, "OpenVault", "OnTrigger", "VaultDor", "Close"),
        ],
    };

    public static LogicEventInfo Event(
        long number,
        EntityTraceKind kind,
        string source,
        string output,
        string target,
        string input) =>
        new()
        {
            Number = number,
            Tick = Tick,
            Kind = kind,
            SourceName = source,
            Output = output,
            TargetName = target,
            Input = input,
            Parameter = "",
        };
}
