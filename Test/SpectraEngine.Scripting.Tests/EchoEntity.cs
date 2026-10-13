using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// An entity with an input and an output for every type a wire carries. It
// reports the text each input got where scripts print, and fires the
// output of that type with the same text. So a test sees what a script's
// value is on a wire, and what a wire's text is to a script.
internal sealed class EchoEntity(IScriptSink sink) : Entity
{
    public const string Class = "echo";

    // A second class of the same entity, whose names clash with members of
    // a node and with its children.
    public const string ClashingClass = "clasher";

    // An input, the output it fires, and what both carry.
    public static readonly (string Input, string Output, ParameterType Carries)[] Ports =
    [
        ("Ping", "OnPing", ParameterType.None),
        ("SayFlag", "OnFlag", ParameterType.Of(KeyvalueType.Bool)),
        ("SayWhole", "OnWhole", ParameterType.Of(KeyvalueType.Int)),
        ("SayNumber", "OnNumber", ParameterType.Of(KeyvalueType.Float)),
        ("SayText", "OnText", ParameterType.Of(KeyvalueType.String)),
        ("SayName", "OnName", ParameterType.Of(KeyvalueType.TargetName)),
        ("SayPair", "OnPair", ParameterType.Of(KeyvalueType.Vec2)),
        ("SayPoint", "OnPoint", ParameterType.Of(KeyvalueType.Vec3)),
        ("SayFour", "OnFour", ParameterType.Of(KeyvalueType.Vec4)),
        ("SayColor", "OnColor", ParameterType.Of(KeyvalueType.Color)),
        ("SayAngles", "OnAngles", ParameterType.Of(KeyvalueType.Angles)),
        ("SayNode", "OnNode", ParameterType.Of(KeyvalueType.NodeRef)),
        ("SayMaybe", "OnMaybe", ParameterType.Optional(KeyvalueType.Float)),
        ("SayAny", "OnAny", ParameterType.NotSaid),

        // The two that report nothing, for a test that counts what a call costs.
        ("Tap", "OnTapped", ParameterType.None),
        ("Nudge", "OnNudged", ParameterType.None),
    ];

    public static EntitySchema Schema { get; } = new(
        Class,
        displayName: "Echo",
        group: "Tests",
        inputs: [.. Ports.Select(port => new EntityPort(port.Input, port.Carries))],
        outputs: [.. Ports.Select(port => new EntityPort(port.Output, port.Carries))]);

    // Lid and Hatch are also children of the node a test puts it on.
    public static EntitySchema ClashingSchema { get; } = new(
        ClashingClass,
        placement: EntityPlacement.Brush,
        inputs: EntityPort.Named("GetFullName", "Name", "Lid", "Shut"),
        outputs: EntityPort.Named("Parent", "IsA", "Hatch", "Shut", "OnShut"));

    public override bool AcceptInput(string input, ref EntityInputContext context)
    {
        if (input is not ("Tap" or "Nudge"))
            sink.Report(new ScriptMessage(ScriptSeverity.Print, $"{TargetName} got {input} '{context.Parameter}'", string.Empty, 0));

        foreach ((string name, string output, _) in Ports)
        {
            if (string.Equals(name, input, StringComparison.Ordinal))
                FireOutput(output, context.Activator, context.Parameter);
        }

        return true;
    }
}
