using System.Numerics;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

internal static class EntityMotion
{
    public const float Dt = 1f / 60f;

    // EntityRuntime's classes plus the ones that tick and move.
    public static EntityCatalog Catalog(List<string> log)
    {
        EntityCatalog catalog = EntityRuntime.Catalog(log);
        catalog.Add(new EntitySchema("slider"), () => new SliderEntity());
        catalog.Add(new EntitySchema("ticker"), () => new TickingEntity(log));
        catalog.Add(new EntitySchema("owner"), () => new OwningEntity());
        return catalog;
    }

    // A slider that travels by offset over this many ticks, then comes back.
    public static SceneNode Slider(SceneNode node, Vector3 offset, int ticks = 20)
    {
        node.Entity = new EntityData("slider");
        node.Entity.SetValue("offset", KeyvalueWire.Format(offset));
        node.Entity.SetValue("ticks", KeyvalueWire.Format(ticks));
        return node;
    }

    public static SceneNode BrushNode(SceneNode parent, string name, BrushKind kind, Vector3 position)
    {
        SceneNode node = parent.CreateChild(name);
        node.LocalPosition = position;

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = kind;
        node.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));
        return node;
    }

    // Every bit of a transform, so -0 and 0 or two NaNs cannot pass as equal.
    public static byte[] Bits(Transform transform) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<Transform>(in transform)).ToArray();
}

// Slides its node from the authored pose to an offset one and back, for as
// long as the world runs. Progress is a tick count, never an accumulated sum.
internal sealed class SliderEntity : Entity
{
    private Transform _closed;
    private Transform _open;
    private Vector3 _offset = new(0f, 2f, 0f);
    private float _turn;
    private float _grow = 1f;
    private int _travelTicks = 20;
    private int _ticksTravelled;
    private int _direction = 1;

    public bool LastMoveAccepted { get; private set; } = true;

    // The node's transform as OnRemove saw it.
    public Transform? SeenOnRemove { get; private set; }

    // Set to move the node from inside OnRemove.
    public Vector3? MoveOnRemove { get; set; }

    public override bool ParseKeyValue(string key, string value)
    {
        switch (key)
        {
            case "offset": return KeyvalueWire.TryParseVec3(value, out _offset);
            case "turn": return KeyvalueWire.TryParseFloat(value, out _turn);
            case "grow": return KeyvalueWire.TryParseFloat(value, out _grow);
            case "ticks": return KeyvalueWire.TryParseInt(value, out _travelTicks);
            default: return false;
        }
    }

    protected internal override void OnSpawn()
    {
        _closed = Node.LocalTransform;
        _open = new Transform
        {
            Position = _closed.Position + _offset,
            Rotation = _closed.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, _turn),
            Scale = _closed.Scale * _grow,
        };

        SetTicking(true);
    }

    protected internal override void OnTick()
    {
        _ticksTravelled += _direction;
        if (_ticksTravelled == 0 || _ticksTravelled == _travelTicks)
            _direction = -_direction;

        // The stored poses at both ends, so the ends are exact.
        Transform pose = _ticksTravelled == 0 ? _closed
            : _ticksTravelled == _travelTicks ? _open
            : Between((float)_ticksTravelled / _travelTicks);

        LastMoveAccepted = MoveNode(in pose);
    }

    protected internal override void OnRemove()
    {
        SeenOnRemove = Node.LocalTransform;

        if (MoveOnRemove is { } position)
        {
            Transform moved = Node.LocalTransform;
            moved.Position = position;
            MoveNode(in moved);
        }
    }

    private Transform Between(float t) => new()
    {
        Position = Vector3.Lerp(_closed.Position, _open.Position, t),
        Rotation = Quaternion.Slerp(_closed.Rotation, _open.Rotation, t),
        Scale = Vector3.Lerp(_closed.Scale, _open.Scale, t),
    };
}

// Logs each tick and each input it gets. "ticking" = 1 starts it ticking at
// spawn; the inputs Start and Stop switch it.
internal sealed class TickingEntity : Entity
{
    private readonly List<string> _log;
    private bool _tickFromSpawn;

    public TickingEntity(List<string> log) => _log = log;

    public override bool ParseKeyValue(string key, string value) =>
        key == "ticking" && KeyvalueWire.TryParseBool(value, out _tickFromSpawn);

    protected internal override void OnSpawn()
    {
        _log.Add($"spawn:{TargetName}:{World.TickNumber}");
        SetTicking(_tickFromSpawn);
    }

    protected internal override void OnTick() => _log.Add($"tick:{TargetName}:{World.TickNumber}");

    public override bool AcceptInput(string input, ref EntityInputContext context)
    {
        _log.Add($"input:{TargetName}:{input}:{World.TickNumber}");

        if (input == "Start") SetTicking(true);
        else if (input == "Stop") SetTicking(false);
        else return false;

        return true;
    }
}

// Opens CollectOwnedBrushes to a test.
internal sealed class OwningEntity : Entity
{
    public IReadOnlyList<SceneNode> OwnedBrushes() => CollectOwnedBrushes();
}
