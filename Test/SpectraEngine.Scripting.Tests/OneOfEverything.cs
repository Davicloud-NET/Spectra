using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

// A small level with one of everything a level holds, on top of the
// baseplate level's sun and ground: parts in a group, a part under a turned
// and scaled parent, a light, a point entity with a wire, a brush entity
// and a part with a script.
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
        level.Part("CrateA", new Vector3(-0f, 0.25f, 2f), crates);
        level.Part("CrateB", new Vector3(1f, 0.25f, -0f), crates).LocalRotation =
            Quaternion.CreateFromYawPitchRoll(0.5f, 0f, 0f);

        SceneNode rig = root.CreateChild("Rig");
        rig.LocalTransform = new Transform
        {
            Position = new Vector3(-6f, 1f, 3f),
            Rotation = Quaternion.CreateFromYawPitchRoll(0.4f, -0.3f, 0.2f),
            Scale = new Vector3(2f, 0.5f, 1.5f),
        };
        level.Part("Arm", new Vector3(1f, 2f, -3f), rig);

        SceneNode lamp = root.CreateChild("Lamp");
        lamp.LocalPosition = new Vector3(0f, 4f, 0f);
        lamp.Light = new Light { Kind = LightKind.Point, Color = new Vector3(1f, 0.8f, 0.6f), Range = 12f };

        SceneNode relay = root.CreateChild("Relay");
        relay.LocalPosition = new Vector3(2f, 1f, 2f);
        relay.Entity = new EntityData(SayingEntity.Class);
        relay.Entity.SetValue("note", "kept as it is");
        relay.Entity.Connections.Add(new EntityConnection(SayingEntity.Said, "Gate", "Say", "heard", 0f, 3));

        level.Part("Gate", new Vector3(-2f, 1f, -4f)).Entity = new EntityData(SayingEntity.Class);

        level.AddScript(level.Part("Spinner", new Vector3(6f, 2f, 6f)), Spin, "Spin");
    }

    // Sends the relay a Say, so its wire fires and counts one use down. By
    // its node and not its name, which a script may have changed.
    public static void FireTheWire(ScriptLevel level)
    {
        EntityWorld world = level.Entities;
        SceneNode relay = level.Scene.EntityNodes.First(node => node.Entity is { Connections.Count: > 0 });
        if (world.Index is { } index && index.TryGetByNodeId(relay.Id, out Entity? entity))
            world.QueueInput(entity, "Say", "go");
    }
}
