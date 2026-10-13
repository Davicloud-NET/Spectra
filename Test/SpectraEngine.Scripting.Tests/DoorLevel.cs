using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;
using SpectraEngine.Entities;

namespace SpectraEngine.Scripting.Tests;

// The level the site's two door pages build: the built-in door and button,
// named Door and Button, with no wire between them.
internal static class DoorLevel
{
    // A press every 12 ticks. The button is back out two ticks after one.
    public const int TicksPerPress = 12;

    // The file Insert Script makes for a part that is called Door.
    public const string DoorScript = "Scripts/Door.luau";

    // Away from where the player starts, so the door squeezes nobody.
    public static ScriptLevel Build(out SceneNode button, out SceneNode door, string? doorSpeed = "6")
    {
        var level = new ScriptLevel();

        button = level.Part("Button", new Vector3(20f, 1f, 20f));
        var buttonData = new EntityData("func_button");
        buttonData.SetValue("distance", "0.1");
        buttonData.SetValue("speed", "6");
        buttonData.SetValue("wait", "0");
        button.Entity = buttonData;

        door = level.Part("Door", new Vector3(24f, 1f, 20f));
        var doorData = new EntityData("func_door");
        doorData.SetValue("distance", "0.5");
        doorData.SetValue("wait", "-1");
        if (doorSpeed is not null)
            doorData.SetValue("speed", doorSpeed);

        door.Entity = doorData;
        return level;
    }

    // The counter of the pages: it is full at three.
    public static void AddCounter(ScriptLevel level, bool opensTheDoor)
    {
        var data = new EntityData("math_counter");
        data.SetValue("max", "3");
        if (opensTheDoor)
            data.Connections.Add(Wire(MathCounter.OnHitMax, "Door", "Open"));

        level.Scene.Root.CreateChild("Presses").Entity = data;
    }

    public static EntityConnection Wire(string output, string target, string input) =>
        new(output, target, input, string.Empty, 0f, EntityConnection.Infinite);

    // What the player's use key sends.
    public static void Press(ScriptLevel level, SceneNode button) =>
        level.Entities.QueueInput(
            level.Entities.Entities.OfType<FuncButton>().Single(entity => ReferenceEquals(entity.Node, button)),
            PlaySession.UseInput);

    public static FuncDoor Live(ScriptLevel level, SceneNode door) =>
        level.Entities.Entities.OfType<FuncDoor>().Single(entity => ReferenceEquals(entity.Node, door));
}
