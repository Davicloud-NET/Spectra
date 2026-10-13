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
    private readonly EntityWorld _world;

    // In the order the touches began.
    private readonly List<Touch> _touches = [];
    private readonly List<Change> _changes = [];
    private long _pass;
    private float _solidReach;

    // What the mover says of the last tick: the part the player stands on
    // and the parts its slide stopped at.
    private Guid _ground;
    private CharacterSlideContacts _ranInto;

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

    // What the mover knows is taken from it. The part the player stands on
    // and the parts its slide stopped at are touched, wherever the tick left
    // the player: off a corner the slide ends well clear of what it hit.
    public void BeginPass(IPlayerPresence? player)
    {
        _pass++;
        _changes.Clear();
        _ground = default;
        _ranInto = default;
        if (player is not { IsPresent: true })
            return;

        // The mover stops a skin off and counts as contact what is within two.
        _solidReach = 2f * player.SkinWidth;

        CharacterState state = player.State;
        _ranInto = state.SlideContacts;
        if (state.Grounded && !_ranInto.Contains(state.GroundNodeId))
            _ground = state.GroundNodeId;

        Hold(_ground);
        for (int i = 0; i < _ranInto.Count; i++)
            Hold(_ranInto[i]);
    }

    // Whether the pass has to measure the node: it is touched or a script
    // listens to it, and the mover has not answered for it already.
    public bool Watches(SceneNode node) =>
        node.Id != _ground && !_ranInto.Contains(node.Id) && (IndexOf(node.Id) >= 0 || IsHeard(node.Id));

    // A node Watches said yes to, with CanTouch on. The distance is from the
    // capsule's surface to its shape.
    public void Measured(SceneNode node, float distance)
    {
        // The mover never lets the player into what is solid, so a solid
        // part is touched from where the mover counts it as contact.
        // Anything else is touched by overlap, as a trigger is.
        float reach = node.CanCollide && node.Brush is not null ? _solidReach : 0f;
        Decide(node, distance <= reach, distance <= reach + TouchTracker.ExitMargin);
    }

    // By its id: the slide may have left the player out of reach of the
    // pass's own search. What that search leaves out is left out here.
    private void Hold(Guid id)
    {
        if (id == default || !_world.Scene.TryFindById(id, out SceneNode? node))
            return;

        if (node.CanTouch && TouchTracker.SensorFilter.Accepts(node))
            Decide(node, isTouching: true, isHolding: true);
    }

    // A touch that goes on is marked as seen while it holds. One that is
    // not going on starts when the node is touched and a script listens.
    private void Decide(SceneNode node, bool isTouching, bool isHolding)
    {
        int at = IndexOf(node.Id);
        if (at >= 0)
        {
            if (isHolding)
                _touches[at] = new Touch(node.Id, node.Name, _pass);

            return;
        }

        if (isTouching && IsHeard(node.Id))
            _changes.Add(new Change(node.Id, node.Name, true, OldestListener(node.Id)));
    }

    // Queues a call for each listener of every touch the pass found started
    // or ended. Ends go first, and each kind goes by the oldest listener a
    // part has, which is the same on every run.
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
