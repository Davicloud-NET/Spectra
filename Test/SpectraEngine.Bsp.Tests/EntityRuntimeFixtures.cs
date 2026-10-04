using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// Each test gets its own catalogue: EntityCatalog.Shared freezes on first read
// and refuses duplicate class names, which would make tests order-dependent.
internal static class EntityRuntime
{
    public static EntityCatalog Catalog(List<string> log)
    {
        var catalog = new EntityCatalog();
        catalog.Add(new EntitySchema("recorder"), () => new RecordingEntity(log));
        catalog.Add(new EntitySchema("relay"), () => new RelayEntity());
        catalog.Add(new EntitySchema("speedster"), () => new SpeedEntity());
        catalog.Add(new EntitySchema("lifecycle"), () => new LifecycleEntity(log));
        return catalog;
    }

    public static SceneNode Place(SceneNode parent, string name, string className)
    {
        SceneNode node = parent.CreateChild(name);
        node.Entity = new EntityData(className);
        return node;
    }

    public static void Wire(
        SceneNode node,
        string output,
        string targetName,
        string input,
        string parameter = "",
        float delay = 0f,
        int timesToFire = EntityConnection.Infinite) =>
        node.Entity!.Connections.Add(
            new EntityConnection(output, targetName, input, parameter, delay, timesToFire));

    public static Entity Live(EntityWorld world, SceneNode node)
    {
        world.Index.ShouldNotBeNull();
        world.Index!.TryGetByNodeId(node.Id, out Entity? entity).ShouldBeTrue();
        return entity!;
    }
}

// Logs one line per input received, so tests can assert delivery order.
internal sealed class RecordingEntity : Entity
{
    private readonly List<string> _log;

    public RecordingEntity(List<string> log) => _log = log;

    // Tells apart two entities that share a name.
    public string Tag { get; private set; } = "";

    public string Label => Tag.Length > 0 ? Tag : TargetName;

    public override bool ParseKeyValue(string key, string value)
    {
        if (!string.Equals(key, "tag", StringComparison.Ordinal))
            return false;

        Tag = value;
        return true;
    }

    public override bool AcceptInput(string input, ref EntityInputContext context)
    {
        _log.Add(
            $"{Label}:{input}:{context.Parameter}:" +
            $"{context.Activator?.TargetName ?? "-"}:{context.Caller?.TargetName ?? "-"}");
        return true;
    }
}

// Logs one line per lifecycle callback.
internal sealed class LifecycleEntity : Entity
{
    private readonly List<string> _log;

    public LifecycleEntity(List<string> log) => _log = log;

    protected internal override void OnSpawn() => _log.Add($"spawn:{TargetName}");

    protected internal override void OnActivate() => _log.Add($"activate:{TargetName}");

    protected internal override void OnRemove() => _log.Add($"remove:{TargetName}");
}

// Fires OnTrigger for each Trigger input.
internal sealed class RelayEntity : Entity
{
    public int Triggers { get; private set; }

    public override bool AcceptInput(string input, ref EntityInputContext context)
    {
        if (!string.Equals(input, "Trigger", StringComparison.Ordinal))
            return false;

        Triggers++;
        FireOutput("OnTrigger", context.Activator);
        return true;
    }
}

internal sealed class SpeedEntity : Entity
{
    public float Speed { get; private set; } = 100f;

    public override bool ParseKeyValue(string key, string value)
    {
        if (!string.Equals(key, "speed", StringComparison.Ordinal))
            return false;

        if (!KeyvalueWire.TryParseFloat(value, out float parsed))
        {
            // Known key, unreadable value: keep the default.
            RefuseKeyvalue(key, value);
            return true;
        }

        Speed = parsed;
        return true;
    }
}
