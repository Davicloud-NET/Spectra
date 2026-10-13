using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The functions scripts have connected to what a level's nodes fire, by node
/// and by name. The script runtime adds and removes them. A fired output
/// queues one event for each, and the runtime gets the listener back through
/// <see cref="IScriptRuntime.Hear"/> when that event comes due. Render thread only.
/// </summary>
public sealed class ScriptListeners
{
    // A list stays once it is empty: a thread that waits for a signal in a
    // loop adds and removes one listener each time round.
    private readonly Dictionary<Port, List<long>> _byPort = [];

    // What fired while the level spawned, when no script ran yet to listen.
    private readonly HashSet<Port> _missed = [];

    internal ScriptListeners()
    {
    }

    /// <summary>How many listeners there are in the whole level.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Adds a listener to what <paramref name="node"/> fires under
    /// <paramref name="name"/>. Listeners of one name are called in the order
    /// they were added.
    /// </summary>
    /// <param name="listener">What the runtime wants handed back for each fire.</param>
    public void Add(SceneNode node, string name, long listener)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(name);

        var port = new Port(node.Id, name);
        if (!_byPort.TryGetValue(port, out List<long>? listeners))
        {
            listeners = [];
            _byPort.Add(port, listeners);
        }

        listeners.Add(listener);
        Count++;
    }

    /// <summary>
    /// Removes a listener. A fire that is already queued for it still comes,
    /// and the runtime drops it. False when it was not there.
    /// </summary>
    public bool Remove(SceneNode node, string name, long listener)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(name);

        if (!_byPort.TryGetValue(new Port(node.Id, name), out List<long>? listeners) || !listeners.Remove(listener))
            return false;

        Count--;
        return true;
    }

    /// <summary>How many listeners what <paramref name="node"/> fires under <paramref name="name"/> has.</summary>
    public int CountOf(SceneNode node, string name)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(name);

        return Find(node, name)?.Count ?? 0;
    }

    /// <summary>
    /// Whether <paramref name="node"/> fired <paramref name="name"/> while the
    /// level spawned. Scripts start after that, so none heard it. True once
    /// for each node and name: the runtime tells the first script that
    /// connects, and no other.
    /// </summary>
    public bool TakeMissedFire(SceneNode node, string name)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(name);

        return _missed.Count > 0 && _missed.Remove(new Port(node.Id, name));
    }

    internal void NoteMissedFire(SceneNode node, string name) => _missed.Add(new Port(node.Id, name));

    // The list itself. Queueing an event runs no script, so it holds still
    // while a fire walks it.
    internal List<long>? Find(SceneNode node, string name) =>
        Count > 0 && _byPort.TryGetValue(new Port(node.Id, name), out List<long>? listeners) ? listeners : null;

    internal void Clear()
    {
        _byPort.Clear();
        _missed.Clear();
        Count = 0;
    }

    private readonly record struct Port(Guid Node, string Name);
}
