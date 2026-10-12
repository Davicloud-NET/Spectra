using System.Numerics;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

// A level with every kind of node a script can meet, beside the level's own
// Sun and Baseplate. House holds Walls (North, South), Door (an entity, with
// Hinge), Lamp (with Shade) and Sign. Panel, Dish, Relay and Rules sit under
// the root, and so does the test's script, Main.
internal static class House
{
    public const string ScriptName = "Main";

    public static SceneNode Build(ScriptLevel level)
    {
        SceneNode root = level.Scene.Root;
        SceneNode house = root.CreateChild("House");
        SceneNode walls = house.CreateChild("Walls");
        level.Part("North", new Vector3(0f, 1f, -4f), walls);
        level.Part("South", new Vector3(0f, 1f, 4f), walls);

        // A part that is an entity, as a door is.
        SceneNode door = level.Part("Door", new Vector3(2f, 1f, 0f), house);
        door.Entity = new EntityData(SayingEntity.Class);
        level.Part("Hinge", new Vector3(0.5f, 0f, 0f), door);

        SceneNode lamp = Light(house, "Lamp", LightKind.Point);
        Light(lamp, "Shade", LightKind.Spot);
        SpatialTestHelpers.CreateMeshNode(house, "Sign", new Vector3(-2f, 1f, 0f));

        Light(root, "Panel", LightKind.Rect);
        Light(root, "Dish", LightKind.Disc);

        // An entity with nothing else, and a module.
        root.CreateChild("Relay").Entity = new EntityData(SayingEntity.Class);
        root.CreateChild("Rules").Script = new Script("Scripts/rules.luau") { IsModule = true };
        return house;
    }

    // A level with the house in it and one script under the root.
    public static ScriptLevel With(string script)
    {
        var level = new ScriptLevel();
        Build(level);
        level.AddScript(level.Scene.Root, script, ScriptName);
        return level;
    }

    // Plays the house for a tick and hands back what its script printed.
    public static string[] Ask(string script)
    {
        using ScriptLevel level = With(script);
        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        return level.Printed;
    }

    private static SceneNode Light(SceneNode parent, string name, LightKind kind)
    {
        SceneNode node = parent.CreateChild(name);
        node.Light = new Light { Kind = kind };
        return node;
    }
}
