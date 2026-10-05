using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

internal static class Touching
{
    public const float Dt = EntityMotion.Dt;

    // EntityMotion's classes plus one that senses.
    public static EntityCatalog Catalog(List<string> log)
    {
        EntityCatalog catalog = EntityMotion.Catalog(log);
        catalog.Add(new EntitySchema("sensor"), () => new TouchSensorEntity(log));
        return catalog;
    }

    // A volume as the editor stamps one: a part that is not drawn, not solid
    // and hidden from queries, with touch left on.
    public static SceneNode Volume(SceneNode parent, string name, Vector3 center, Vector3 halfSize)
    {
        SceneNode node = parent.CreateChild(name);
        node.LocalPosition = center;
        node.Entity = new EntityData("sensor");
        Stamp(node, halfSize);
        return node;
    }

    public static void Stamp(SceneNode node, Vector3 halfSize)
    {
        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(-halfSize, halfSize);
        node.CanCollide = false;
        node.CanQuery = false;
        node.IsRendered = false;
    }

    public static EntityWorld Play(Scene scene, List<string> log, FakePlayerPresence? player, CapturingLogger? logger = null)
    {
        var world = new EntityWorld(scene, logger ?? new CapturingLogger(), Catalog(log));
        world.Activate();
        world.Player = player;
        return world;
    }
}

// Senses with the brushes it owns, logs each touch and fires an output for
// it. The inputs Enable and Disable switch its sensing. "once" = 1 makes it
// switch itself off from inside the callback that starts a touch.
internal sealed class TouchSensorEntity : Entity, ITouchListener
{
    private readonly List<string> _log;
    private bool _once;

    public TouchSensorEntity(List<string> log) => _log = log;

    public override bool ParseKeyValue(string key, string value) =>
        key == "once" && KeyvalueWire.TryParseBool(value, out _once);

    protected internal override void OnSpawn() => World.Touches.Register(this, CollectOwnedBrushes());

    public void OnTouchStarted(in TouchVisitor visitor)
    {
        _log.Add($"start:{TargetName}");
        FireOutput("OnStartTouch");

        if (_once)
            World.Touches.SetSensing(this, false);
    }

    public void OnTouchEnded(in TouchVisitor visitor)
    {
        _log.Add($"end:{TargetName}");
        FireOutput("OnEndTouch");
    }

    public override bool AcceptInput(string input, ref EntityInputContext context)
    {
        if (input == "Enable") World.Touches.SetSensing(this, true);
        else if (input == "Disable") World.Touches.SetSensing(this, false);
        else return false;

        return true;
    }
}
