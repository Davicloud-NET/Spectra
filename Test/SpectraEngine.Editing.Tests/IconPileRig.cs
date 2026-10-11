using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

// A viewport twelve units back from the origin, and the things that pile up
// in a level: sounds, a relay, a player start and lamps. Near the origin an
// icon is as big as icons get.
internal sealed class IconPileRig
{
    public const string Sound = "test_sound";
    public const string Relay = "test_relay";
    public const string Start = "test_start";

    public static readonly Vector3 SoundTint = EntityGroupTints.For("Sound");
    public static readonly Vector3 RelayTint = EntityGroupTints.For("Logic");
    public static readonly Vector3 StartTint = EntityGroupTints.For("Player");

    // Full strength in one channel and none too dark, so a lamp's icon is
    // this colour as it stands.
    public static readonly Vector3 LampTint = new(1f, 0.5f, 0.4f);

    private static readonly EntitySchemaCatalog Schemas = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema(Sound, group: "Sound", icon: "sound"),
        new EntitySchema(Relay, group: "Logic", placement: EntityPlacement.Abstract, icon: "relay"),
        new EntitySchema(Start, group: "Player", icon: "player-start", hasFacing: true),
    ]));

    public IconPileRig()
    {
        Scene.EntitySchemas = Schemas;
        Camera.Position = new Vector3(0f, 0f, 12f);
        Camera.LookAt(Vector3.Zero);
    }

    public ViewportHarness Harness { get; } = new();

    public Scene Scene => Harness.Scene;

    public Camera Camera => Harness.Scene.Camera;

    public Vector2 ViewportSize => Harness.ViewportSize;

    public SceneNode AddEntity(string className, Vector3 position)
    {
        var node = new SceneNode(className) { LocalPosition = position, Entity = new EntityData(className) };
        Scene.Root.AddChild(node);
        return node;
    }

    public SceneNode AddLamp(Vector3 position, LightKind kind = LightKind.Point)
    {
        var node = new SceneNode("Lamp")
        {
            LocalPosition = position,
            Light = new Light { Kind = kind, Color = LampTint, Range = 3f },
        };

        Scene.Root.AddChild(node);
        return node;
    }

    // What the demo's lift carries at one point: three sounds and a relay.
    public SceneNode[] AddLift(Vector3 position) =>
    [
        AddEntity(Sound, position),
        AddEntity(Sound, position),
        AddEntity(Sound, position),
        AddEntity(Relay, position),
    ];

    // Its own set each time: where an icon stands follows from the scene and
    // the camera, so every set gives the same answer.
    public SceneIcons Place()
    {
        var icons = new SceneIcons();
        icons.Place(Scene, Camera, ViewportSize);
        return icons;
    }

    public Ray3 RayThrough(Vector3 world) =>
        Camera.ScreenPointToRay(Harness.WorldToScreen(world), ViewportSize);

    public SceneNode? Pick(Vector3 through)
    {
        Ray3 ray = RayThrough(through);
        return new SceneIcons().TryPick(Scene, Camera, in ray, ViewportSize, out SceneNode? node, out _) ? node : null;
    }

    public SceneNode? Click(Vector3 on)
    {
        Vector2 at = Harness.WorldToScreen(on);
        Scene.Selection.Clear();
        Harness.Press(at);
        Harness.Release(at);
        return Scene.Selection.Count == 1 ? Scene.Selection.Items[0] : null;
    }
}
