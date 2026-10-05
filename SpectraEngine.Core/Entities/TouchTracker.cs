using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Finds, once a tick, which sensing entities the player is inside, and tells
/// each one when a touch starts and when it ends. An entity senses with the
/// part brushes it registered that have <see cref="SceneNode.CanTouch"/>,
/// each tested as its real shape. What one tick finds is told in entity order.
/// </summary>
// Runs from the visitor: one box query, then the brushes it returns. A level
// full of triggers costs what the ones near the player cost.
public sealed class TouchTracker
{
    /// <summary>
    /// How far clear of a sensor a visitor must be before the touch ends. A
    /// touch starts at contact, so the gap keeps a capsule at rest on a face
    /// from starting and ending a touch every tick.
    /// </summary>
    public const float ExitMargin = 0.05f;

    // A sensor has CanQuery off, and a world brush never senses.
    private static readonly SceneQueryFilter SensorFilter =
        new() { IgnoreQueryFlags = true, ExcludeStaticWorldBrushes = true };

    private readonly EntityWorld _world;

    private readonly Dictionary<Entity, Sensor> _sensors = new(ReferenceEqualityComparer.Instance);

    // By node id, not reference: a node deleted and restored is a new object
    // under the old id.
    private readonly Dictionary<Guid, Sensor> _sensorByNode = [];

    // In the order the touches began.
    private readonly List<Touch> _touches = [];

    // Reused by every pass.
    private readonly List<SceneNode> _candidates = [];
    private readonly List<Sensor> _near = [];
    private readonly List<TouchEvent> _events = [];
    private long _pass;

    internal TouchTracker(EntityWorld world) => _world = world;

    /// <summary>How many entities are registered to sense.</summary>
    public int SensorCount => _sensors.Count;

    /// <summary>How many touches are going on.</summary>
    public int TouchCount => _touches.Count;

    /// <summary>
    /// Makes <paramref name="entity"/> sense with <paramref name="brushes"/>.
    /// Call it from <see cref="Entity.OnSpawn"/> with what
    /// <see cref="Entity.CollectOwnedBrushes"/> returns. Calling again
    /// replaces the brushes and keeps the touches.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The entity is not an <see cref="ITouchListener"/>, or is not running in this world.
    /// </exception>
    public void Register(Entity entity, IReadOnlyList<SceneNode> brushes)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(brushes);

        if (entity is not ITouchListener listener)
        {
            throw new ArgumentException(
                "Only an entity that implements ITouchListener can sense.", nameof(entity));
        }

        // Events fire in entity order, so a sensor needs a place in it.
        if (OrderOf(entity) < 0)
        {
            throw new ArgumentException(
                "The entity is not running in this entity world.", nameof(entity));
        }

        if (_sensors.TryGetValue(entity, out Sensor? sensor))
        {
            ForgetBrushes(sensor);
        }
        else
        {
            sensor = new Sensor(entity, listener);
            _sensors.Add(entity, sensor);
        }

        bool hasPart = false;
        for (int i = 0; i < brushes.Count; i++)
        {
            SceneNode node = brushes[i];
            sensor.NodeIds.Add(node.Id);
            _sensorByNode[node.Id] = sensor;
            hasPart |= node.Brush is not null && node.BrushKind == BrushKind.Part;
        }

        if (!hasPart)
            _world.ReportWarning(entity, "has no part brush to sense with, so nothing can touch it");
    }

    /// <summary>
    /// Switches a registered entity's sensing on or off. Switching off ends
    /// its touches before this returns, and each one is told through
    /// <see cref="ITouchListener.OnTouchEnded"/>. Switching on with a visitor
    /// already inside starts a touch on the next tick.
    /// </summary>
    public void SetSensing(Entity entity, bool on)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (!_sensors.TryGetValue(entity, out Sensor? sensor) || sensor.IsSensing == on)
            return;

        sensor.IsSensing = on;
        if (!on)
            EndTouches(sensor);
    }

    // The touch pass. Poses are the ones the last tick left.
    internal void Update()
    {
        _events.Clear();
        if (_sensors.Count == 0)
            return;

        IPlayerPresence? player = _world.Player;
        var visitor = new TouchVisitor(TouchVisitor.PlayerId, player);

        _pass++;
        _near.Clear();
        if (player is { IsPresent: true })
            MeasureNearSensors(player.Capsule);

        for (int i = 0; i < _touches.Count; i++)
        {
            Touch touch = _touches[i];
            if (touch.Visitor.Id != visitor.Id)
                continue;

            // Not measured means its brushes are out of reach, gone or
            // switched off, or the visitor left the level.
            if (touch.Sensor.Pass != _pass || touch.Sensor.Distance > ExitMargin)
                _events.Add(new TouchEvent(touch.Sensor, touch.Visitor, false, OrderOf(touch.Sensor.Entity)));
        }

        for (int i = 0; i < _near.Count; i++)
        {
            Sensor sensor = _near[i];
            if (sensor.Distance <= 0f && FindTouch(sensor, visitor.Id) < 0)
                _events.Add(new TouchEvent(sensor, visitor, true, OrderOf(sensor.Entity)));
        }

        _near.Clear();
        if (_events.Count == 0)
            return;

        SortEventsByEntityOrder();
        FireEvents();
    }

    // A despawn. Its touches end first, so each start gets its end.
    internal void Unregister(Entity entity)
    {
        if (!_sensors.Remove(entity, out Sensor? sensor))
            return;

        ForgetBrushes(sensor);
        sensor.IsRegistered = false;
        EndTouches(sensor);
    }

    // Tells nobody. For a world that is starting or stopping.
    internal void Clear()
    {
        _sensors.Clear();
        _sensorByNode.Clear();
        _touches.Clear();
        _events.Clear();
        _pass = 0;
    }

    private void MeasureNearSensors(in CharacterCapsule capsule)
    {
        // Grown by the margin, so a touch about to end is still measured.
        var reach = new Vector3(capsule.Radius + ExitMargin);
        var box = new Aabb(
            Vector3.Min(capsule.Center1, capsule.Center2) - reach,
            Vector3.Max(capsule.Center1, capsule.Center2) + reach);

        _candidates.Clear();
        _world.Scene.GetPartBoundsInBox(in box, _candidates, in SensorFilter);

        for (int i = 0; i < _candidates.Count; i++)
        {
            SceneNode node = _candidates[i];
            if (!node.CanTouch || node.Brush is not { } brush)
                continue;
            if (!_sensorByNode.TryGetValue(node.Id, out Sensor? sensor) || !sensor.IsSensing)
                continue;

            // A brush node's transform is rigid, so a distance measured in
            // the brush's frame is a world distance.
            if (!Matrix4x4.Invert(node.WorldMatrix, out Matrix4x4 toLocal))
                continue;

            var local = new CharacterCapsule(
                Vector3.Transform(capsule.Center1, toLocal),
                Vector3.Transform(capsule.Center2, toLocal),
                capsule.Radius);
            float distance = CapsuleGeometry.Distance(
                in local, brush.LocalPlaneSpan, brush.LocalFaceSpan, out _, out _);

            if (sensor.Pass != _pass)
            {
                sensor.Pass = _pass;
                sensor.Distance = distance;
                _near.Add(sensor);
            }
            else if (distance < sensor.Distance)
            {
                sensor.Distance = distance;
            }
        }

        _candidates.Clear();
    }

    // The query reports in tree order, which follows the history of inserts
    // and moves. Entity order is the same on every run. Stable, and the list
    // is a handful long.
    private void SortEventsByEntityOrder()
    {
        for (int i = 1; i < _events.Count; i++)
        {
            TouchEvent current = _events[i];
            int j = i - 1;
            while (j >= 0 && _events[j].Order > current.Order)
            {
                _events[j + 1] = _events[j];
                j--;
            }

            _events[j + 1] = current;
        }
    }

    // A listener may switch a sensor off from inside its callback, which ends
    // touches while this runs. So each event is checked again before it fires.
    private void FireEvents()
    {
        for (int i = 0; i < _events.Count; i++)
        {
            TouchEvent due = _events[i];
            Sensor sensor = due.Sensor;
            TouchVisitor visitor = due.Visitor;
            int at = FindTouch(sensor, visitor.Id);

            if (due.Started)
            {
                if (at >= 0 || !sensor.IsRegistered || !sensor.IsSensing)
                    continue;

                _touches.Add(new Touch(sensor, visitor));
                sensor.Listener.OnTouchStarted(in visitor);
            }
            else if (at >= 0)
            {
                _touches.RemoveAt(at);
                sensor.Listener.OnTouchEnded(in visitor);
            }
        }

        _events.Clear();
    }

    private void EndTouches(Sensor sensor)
    {
        for (int i = 0; i < _touches.Count;)
        {
            if (!ReferenceEquals(_touches[i].Sensor, sensor))
            {
                i++;
                continue;
            }

            TouchVisitor visitor = _touches[i].Visitor;
            _touches.RemoveAt(i);
            sensor.Listener.OnTouchEnded(in visitor);

            // The callback may have ended other touches.
            i = 0;
        }
    }

    private void ForgetBrushes(Sensor sensor)
    {
        for (int i = 0; i < sensor.NodeIds.Count; i++)
        {
            Guid id = sensor.NodeIds[i];
            if (_sensorByNode.TryGetValue(id, out Sensor? listed) && ReferenceEquals(listed, sensor))
                _sensorByNode.Remove(id);
        }

        sensor.NodeIds.Clear();
    }

    private int FindTouch(Sensor sensor, int visitorId)
    {
        for (int i = 0; i < _touches.Count; i++)
        {
            if (ReferenceEquals(_touches[i].Sensor, sensor) && _touches[i].Visitor.Id == visitorId)
                return i;
        }

        return -1;
    }

    private int OrderOf(Entity entity)
    {
        IReadOnlyList<Entity> entities = _world.Entities;
        for (int i = 0; i < entities.Count; i++)
        {
            if (ReferenceEquals(entities[i], entity))
                return i;
        }

        return -1;
    }

    private sealed class Sensor(Entity entity, ITouchListener listener)
    {
        public Entity Entity { get; } = entity;

        public ITouchListener Listener { get; } = listener;

        public List<Guid> NodeIds { get; } = [];

        public bool IsSensing { get; set; } = true;

        public bool IsRegistered { get; set; } = true;

        // The pass that last measured one of its brushes, and how near the
        // nearest was.
        public long Pass { get; set; }

        public float Distance { get; set; }
    }

    // One touch is a sensing entity and a visitor id, never a node.
    private readonly record struct Touch(Sensor Sensor, TouchVisitor Visitor);

    private readonly record struct TouchEvent(Sensor Sensor, TouchVisitor Visitor, bool Started, int Order);
}
