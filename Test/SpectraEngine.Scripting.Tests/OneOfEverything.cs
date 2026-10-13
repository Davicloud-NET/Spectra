using System.Numerics;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// A small level with each kind of node a script can write to, on top of the
// baseplate level's sun and ground: parts in a turned group, a light, a mesh
// and a plain node under a scaled one, a point entity with a wire, a brush
// entity, an entity that moves its own node, a trigger, and a script that
// runs, one that is switched off and a module.
internal static class OneOfEverything
{
    private const string Spin = """
        local part = script.Parent
        game:GetService("RunService").Heartbeat:Connect(function(step)
            part.CFrame = part.CFrame * CFrame.Angles(0, step, 0)
        end)
        """;

    public static void AddTo(ScriptLevel level)
    {
        SceneNode root = level.Scene.Root;

        // A zero with a sign is in real maps, and equal to a plain zero only
        // as a number.
        SceneNode crates = root.CreateChild("Crates");
        crates.LocalPosition = new Vector3(4f, 0f, -0f);
        crates.LocalRotation = Quaternion.CreateFromYawPitchRoll(-0.7f, 0f, 0f);
        level.Part("CrateA", new Vector3(-0f, 0.25f, 2f), crates);
        level.Part("CrateB", new Vector3(1f, 0.25f, -0f), crates).LocalRotation =
            Quaternion.CreateFromYawPitchRoll(0.5f, 0f, 0f);

        AddScaledRig(root);
        Lamp(root, "Lamp", new Vector3(0f, 4f, 0f));
        AddEntities(level);

        SceneNode spinner = level.Part("Spinner", new Vector3(6f, 2f, 6f));
        level.AddScript(spinner, Spin, "Spin");
        SceneNode idle = level.AddScript(spinner, Spin, "Idle");
        idle.Script = idle.Script.ShouldNotBeNull() with { IsEnabled = false };
        SceneNode shared = level.AddScript(root, "return {}", "Shared");
        shared.Script = shared.Script.ShouldNotBeNull() with { IsModule = true };

        AddSwitchedOff(level, crates);
    }

    // Under the turned group: lights of two more kinds, one of them off and
    // each with values a new light does not have, and a part with switches
    // off. What a script sets on these has to go back to more than defaults.
    private static void AddSwitchedOff(ScriptLevel level, SceneNode group)
    {
        SceneNode beam = group.CreateChild("Beam");
        beam.LocalPosition = new Vector3(0f, 3f, -0f);
        beam.Light = new Light
        {
            Kind = LightKind.Spot,
            Enabled = false,
            Color = new Vector3(0.25f, -0f, 2f),
            Intensity = 0.1f,
            Range = 7.5f,
        };

        SceneNode panel = group.CreateChild("Panel");
        panel.Light = new Light { Kind = LightKind.Rect, Intensity = 0f, Range = 0.3f };

        SceneNode hidden = level.Part("CrateC", new Vector3(2f, 0.25f, 2f), group);
        hidden.CanQuery = false;
        hidden.CanTouch = false;
    }

    // Sends the relay a Say, so its wire fires and counts one use down. By
    // its node and not its name, which a script may have changed. A script
    // may have taken the relay out of the level too, and then nothing fires.
    public static void FireTheWire(ScriptLevel level)
    {
        EntityWorld world = level.Entities;
        SceneNode? relay = level.Scene.EntityNodes.FirstOrDefault(
            node => node.Entity is { Connections.Count: > 0 } && !TreeWrites.IsMade(node));
        if (relay is not null && world.Index is { } index && index.TryGetByNodeId(relay.Id, out Entity? entity))
            world.QueueInput(entity, "Say", "go");
    }

    // A scale may sit above a light, a mesh or a plain node, and never above
    // a brush.
    private static void AddScaledRig(SceneNode root)
    {
        SceneNode rig = root.CreateChild("Rig");
        rig.LocalTransform = new Transform
        {
            Position = new Vector3(-6f, 1f, 3f),
            Rotation = Quaternion.CreateFromYawPitchRoll(0.4f, -0.3f, 0.2f),
            Scale = new Vector3(2f, 0.5f, 1.5f),
        };

        Lamp(rig, "Bulb", new Vector3(1f, 2f, -3f));
        rig.CreateChild("Mark").LocalPosition = new Vector3(-0f, 0f, 1f);
        SpatialTestHelpers.CreateMeshNode(rig, "Prop", new Vector3(0f, 1f, 0f)).MeshSource =
            new MeshSource("Models/crate.glb", 0);
    }

    private static void AddEntities(ScriptLevel level)
    {
        SceneNode relay = level.Scene.Root.CreateChild("Relay");
        relay.LocalPosition = new Vector3(2f, 1f, 2f);
        relay.Entity = new EntityData(SayingEntity.Class);
        relay.Entity.SetValue("note", "kept as it is");
        relay.Entity.Connections.Add(new EntityConnection(SayingEntity.Said, "Gate", "Say", "heard", 0f, 3));

        level.Part("Gate", new Vector3(-2f, 1f, -4f)).Entity = new EntityData(SayingEntity.Class);
        level.Part("Lift", new Vector3(8f, 0.5f, -0f)).Entity = new EntityData(PacingEntity.Class);

        // A trigger: a brush entity nothing collides with, finds or sees.
        SceneNode zone = level.Part("Zone", new Vector3(-8f, 1f, 0f));
        zone.Entity = new EntityData(SayingEntity.Class);
        zone.CanCollide = false;
        zone.CanQuery = false;
        zone.IsRendered = false;
    }

    private static void Lamp(SceneNode parent, string name, Vector3 at)
    {
        SceneNode lamp = parent.CreateChild(name);
        lamp.LocalPosition = at;
        lamp.Light = new Light { Kind = LightKind.Point, Color = new Vector3(1f, 0.8f, 0.6f), Range = 12f };
    }
}
