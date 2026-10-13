using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

// Which of the parts and meshes scripts listen to the player touches, and
// the call each listener gets when a touch starts and when it ends. The
// listeners are in the level's own table, under the node and a name of
// TouchSignal. TouchTracker runs the pass and hands over what is near.
internal sealed class PartTouches
{
    // For a player that hands out no tuning: the mover's own skin.
    private const float DefaultSkin = 0.010f;

    private readonly EntityWorld _world;

    // In the order the touches began.
    private readonly List<Touch> _touches = [];
    private readonly List<Change> _changes = [];
    private long _pass;
    private float _solidReach = 2f * DefaultSkin;
    private Guid _ground;

    public PartTouches(EntityWorld world) => _world = world;

    public int Count => _touches.Count;

    // How near the player has to be to a solid part to touch it, as of the
    // pass that is running.
    public float SolidReach => _solidReach;

    // False in a level where no script listens for a touch and none has
    // yet to end, which is all such a level pays for a tick.
    public bool HasWork => _world.Listeners.TouchListenerCount > 0 || _touches.Count > 0;

    public void Clear()
    {
        _touches.Clear();
        _changes.Clear();
        _pass = 0;
    }

    // What the mover knows is taken from it: the part the player stands on,
    // and how far off anything solid it stops the player.
    public void BeginPass(IPlayerPresence? player)
    {
        _pass++;
        _changes.Clear();
        _ground = default;
        if (player is not { IsPresent: true })
            return;

        CharacterState state = player.State;
        if (state.Grounded)
            _ground = state.GroundNodeId;

        // The mover counts as contact what is within two skins.
        _solidReach = 2f * ((player as ITunedPlayer)?.Tuning.SkinWidth ?? DefaultSkin);
    }

    // Whether the pass has to measure the node: it is touched, or a script
    // listens to it.
    public bool Watches(SceneNode node) => IndexOf(node.Id) >= 0 || IsHeard(node.Id);

    // The node is near the player and has CanTouch on. The distance is from
    // the capsule's surface to its shape.
    public void Measured(SceneNode node, float distance)
    {
        // The mover never lets the player into what is solid, so a solid
        // part is touched from where the mover stops. Anything else is
        // touched by overlap, as a trigger is.
        float reach = node.CanCollide && node.Brush is not null ? _solidReach : 0f;
        bool isStoodOn = _ground != default && node.Id == _ground;

        int at = IndexOf(node.Id);
        if (at >= 0)
        {
            if (isStoodOn || distance <= reach + TouchTracker.ExitMargin)
                _touches[at] = new Touch(node.Id, node.Name, _pass);

            return;
        }

        if ((isStoodOn || distance <= reach) && IsHeard(node.Id))
            _changes.Add(new Change(node.Id, node.Name, true, OldestListener(node.Id)));
    }

    // Queues a call for each listener of every touch the pass found started
    // or ended. Ends go first, and each kind goes in the order the parts
    // were first listened to, which is the same on every run.
    public void EndPass(in TouchVisitor visitor)
    {
        // Not seen: out of reach, gone, switched off, or nobody walks.
        for (int i = 0; i < _touches.Count; i++)
        {
            Touch touch = _touches[i];
            if (touch.SeenPass != _pass)
                _changes.Add(new Change(touch.Node, touch.Name, false, OldestListener(touch.Node)));
        }

        if (_changes.Count == 0)
            return;

        Sort();
        for (int i = 0; i < _changes.Count; i++)
            Apply(_changes[i], visitor.Id);

        _changes.Clear();
    }

    private void Apply(in Change change, int visitor)
    {
        if (change.Started)
            _touches.Add(new Touch(change.Node, change.Name, _pass));
        else
            _touches.RemoveAt(IndexOf(change.Node));

        string signal = change.Started ? TouchSignal.Touched : TouchSignal.TouchEnded;
        if (_world.Listeners.Find(change.Node, signal) is { Count: > 0 } listeners)
            _world.Dispatcher.QueueTouch(change.Name, signal, visitor, listeners);
    }

    // Stable, and the list is a handful long.
    private void Sort()
    {
        for (int i = 1; i < _changes.Count; i++)
        {
            Change current = _changes[i];
            int j = i - 1;
            while (j >= 0 && Follows(_changes[j], current))
            {
                _changes[j + 1] = _changes[j];
                j--;
            }

            _changes[j + 1] = current;
        }
    }

    private static bool Follows(in Change a, in Change b) =>
        a.Started != b.Started ? a.Started : a.Order > b.Order;

    private bool IsHeard(Guid node) =>
        _world.Listeners.Find(node, TouchSignal.Touched) is { Count: > 0 }
        || _world.Listeners.Find(node, TouchSignal.TouchEnded) is { Count: > 0 };

    // A runtime numbers its listeners as they connect, and a list keeps the
    // order they were added in.
    private long OldestListener(Guid node)
    {
        long oldest = long.MaxValue;
        if (_world.Listeners.Find(node, TouchSignal.Touched) is { Count: > 0 } started)
            oldest = started[0];
        if (_world.Listeners.Find(node, TouchSignal.TouchEnded) is { Count: > 0 } ended)
            oldest = Math.Min(oldest, ended[0]);

        return oldest;
    }

    private int IndexOf(Guid node)
    {
        for (int i = 0; i < _touches.Count; i++)
        {
            if (_touches[i].Node == node)
                return i;
        }

        return -1;
    }

    // By the node's id: one that left the scene still has to hear its end.
    // The name is only for a message.
    private readonly record struct Touch(Guid Node, string Name, long SeenPass);

    private readonly record struct Change(Guid Node, string Name, bool Started, long Order);
}
