using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities;

/// <summary>
/// A volume that moves the player to another entity when the player walks in.
/// </summary>
// The target is looked up at each touch, so it can be spawned or renamed
// while the level runs. When several entities share the name, the first in
// scene order is used. When none has it, nothing moves and one warning is
// logged.
// The player arrives at rest with its feet at the target's origin, facing
// along the target's local +Z. A target that points straight up or down
// leaves the facing alone.
// The trigger is its own activator: the player is not an entity, so
// !activator on one of its wires names the trigger.
[SpectraEntity("trigger_teleport", Group = "Triggers", Placement = EntityPlacement.Volume)]
public sealed partial class TriggerTeleport : Entity, ITouchListener
{
    /// <summary>Fired when the player comes inside. The player has moved by the time it is delivered.</summary>
    [EntityOutput]
    public const string OnStartTouch = nameof(OnStartTouch);

    /// <summary>
    /// Fired when the player is clear of the volume, which after a teleport is
    /// the next tick, or when the trigger is disabled while touched.
    /// </summary>
    [EntityOutput]
    public const string OnEndTouch = nameof(OnEndTouch);

    private readonly TriggerState _state = new();
    private readonly List<Entity> _matches = [];
    private bool _warnedNoTarget;

    /// <summary>The name of the entity the player is moved to.</summary>
    [Keyvalue(
        "target",
        Display = "Destination",
        Tooltip = "The entity the player is moved to, usually an info_teleport_destination.",
        Type = KeyvalueType.TargetName,
        Widget = KeyvalueWidget.EntityPicker)]
    public string Target { get; set; } = "";

    /// <summary>Whether the trigger starts switched off.</summary>
    [Keyvalue(
        "startdisabled",
        Display = "Start disabled",
        Tooltip = "The trigger senses nothing until something sends it Enable.",
        Default = "0")]
    public bool StartDisabled { get; set; }

    /// <summary>Whether the trigger is sensing.</summary>
    public bool IsEnabled => _state.IsEnabled;

    /// <summary>Whether the player is inside.</summary>
    public bool IsTouched => _state.IsTouched;

    /// <summary>How many times the trigger has moved the player since it spawned.</summary>
    public int TeleportCount { get; private set; }

    /// <inheritdoc/>
    protected override void OnSpawn() => _state.Spawn(this, CollectOwnedBrushes(), StartDisabled);

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        state.Add("enabled", IsEnabled);
        state.Add("touched", IsTouched);
        state.Add("teleports", TeleportCount);
    }

    void ITouchListener.OnTouchStarted(in TouchVisitor visitor)
    {
        _state.Started();
        FireOnStartTouch();

        if (visitor.Player is { } player)
            Send(player);
    }

    void ITouchListener.OnTouchEnded(in TouchVisitor visitor)
    {
        _state.Ended();
        FireOnEndTouch();
    }

    [EntityInput("Enable")]
    private void Enable(ref EntityInputContext context) => _state.SetEnabled(this, true);

    [EntityInput("Disable")]
    private void Disable(ref EntityInputContext context) => _state.SetEnabled(this, false);

    [EntityInput("Toggle")]
    private void Toggle(ref EntityInputContext context) => _state.Toggle(this);

    private void Send(IPlayerPresence player)
    {
        _matches.Clear();
        World.Index?.Resolve(Target, this, null, null, _matches);

        if (_matches.Count == 0)
        {
            WarnNoTarget();
            return;
        }

        Matrix4x4 destination = _matches[0].Node.WorldMatrix;
        _matches.Clear();

        TeleportCount++;
        player.Teleport(destination.Translation, FacingYaw(in destination));
    }

    // Once, not once per touch: a player can stand in the doorway all day.
    private void WarnNoTarget()
    {
        if (_warnedNoTarget)
            return;

        _warnedNoTarget = true;
        Warn(Target.Length == 0
            ? "has no destination, so it moves nobody"
            : $"names the destination '{Target}', which matches no entity, so it moves nobody");
    }

    // Yaw zero looks along +X and a quarter turn looks along +Z, the way a
    // character command reads it.
    private static float? FacingYaw(in Matrix4x4 world)
    {
        // The third row is the node's local +Z in world space.
        float x = world.M31;
        float z = world.M33;
        return x * x + z * z > 1e-8f ? MathF.Atan2(z, x) : null;
    }
}
