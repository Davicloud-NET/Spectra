using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The functions scripts have connected to what a level's nodes fire, by node
/// and by name. The script runtime adds and removes them. A fired output
/// queues one event for each, and the runtime gets the listener back through
/// <see cref="IScriptRuntime.Hear"/> when that event comes due. A touch of a
/// part does the same under a name of <see cref="TouchSignal"/>, and comes
/// back through <see cref="IScriptRuntime.HearTouch"/>. Render thread only.
/// </summary>
public sealed class ScriptListeners
{
    // A list stays once it is empty: a thread that waits for a signal in a
    // loop adds and removes one listener each time round.
    private readonly Dictionary<Port, Heard> _byPort = [];

    // What fired while the level spawned, when no script ran yet to listen.
    private readonly HashSet<Port> _missed = [];

    // The nodes a script was told can never be touched in this level.
    private readonly HashSet<Guid> _toldNoTouch = [];
    private readonly ILogger _logger;

    internal ScriptListeners(ILogger logger) => _logger = logger;

    /// <summary>How many listeners there are in the whole level.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// How many of them listen for a touch of a part to start or to end,
    /// under one of the names of <see cref="TouchSignal"/>.
    /// </summary>
    public int TouchListenerCount { get; private set; }

    /// <summary>
    /// Goes up each time a listener is added or removed. A view that lists
    /// them reads it to tell whether its list still holds.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>
    /// Adds a listener to what <paramref name="node"/> fires under
    /// <paramref name="name"/>. Listeners of one name are called in the order
    /// they were added.
    /// </summary>
    /// <param name="listener">What the runtime wants handed back for each fire.</param>
    public void Add(SceneNode node, string name, long listener) => Add(node, name, listener, default);

    /// <summary>The same, for a listener whose script is known.</summary>
    /// <param name="listener">What the runtime wants handed back for each fire.</param>
    /// <param name="by">The script that listens and where it connected. Only a view reads it.</param>
    public void Add(SceneNode node, string name, long listener, in ScriptPlace by)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(name);

        var port = new Port(node.Id, name);
        if (!_byPort.TryGetValue(port, out Heard? heard))
        {
            heard = new Heard();
            _byPort.Add(port, heard);
        }

        heard.Listeners.Add(listener);
        heard.By.Add(by);
        Count++;
        Version++;
        if (!TouchSignal.IsOne(name))
            return;

        TouchListenerCount++;

        // Once for a node: a script tends to listen under both names.
        if (!_toldNoTouch.Contains(node.Id) && TouchSignal.WhyNotHere(node) is { } why)
        {
            _toldNoTouch.Add(node.Id);
            _logger.LogWarning("{Reason}", why);
        }
    }

    /// <summary>
    /// Removes a listener. A fire that is already queued for it still comes,
    /// and the runtime drops it. False when it was not there.
    /// </summary>
    public bool Remove(SceneNode node, string name, long listener)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(name);

        if (!_byPort.TryGetValue(new Port(node.Id, name), out Heard? heard))
            return false;

        int at = heard.Listeners.IndexOf(listener);
        if (at < 0)
            return false;

        heard.Listeners.RemoveAt(at);
        heard.By.RemoveAt(at);
        Count--;
        Version++;
        if (TouchSignal.IsOne(name))
            TouchListenerCount--;

        return true;
    }

    /// <summary>
    /// Lets go of everything kept for what <paramref name="node"/> fires
    /// under <paramref name="name"/>, with any listener still there. For a
    /// node that is gone for good: a list otherwise stays once it is empty.
    /// </summary>
    public void Forget(SceneNode node, string name)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(name);

        var port = new Port(node.Id, name);
        _missed.Remove(port);
        _toldNoTouch.Remove(node.Id);
        if (!_byPort.Remove(port, out Heard? heard))
            return;

        Version++;
        Count -= heard.Listeners.Count;
        if (TouchSignal.IsOne(name))
            TouchListenerCount -= heard.Listeners.Count;
    }

    /// <summary>
    /// Forgets whose listener it is, for one that nothing can call any more,
    /// so a view draws no wire for it. It stays a listener: a fire still
    /// queues an event for it, and a level somebody watches queues what any
    /// other does. False when it was not there, or was hidden already.
    /// </summary>
    public bool Hide(SceneNode node, string name, long listener)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(name);

        if (!_byPort.TryGetValue(new Port(node.Id, name), out Heard? heard))
            return false;

        int at = heard.Listeners.IndexOf(listener);
        if (at < 0 || !heard.By[at].HasNode)
            return false;

        heard.By[at] = default;
        Version++;
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

    /// <summary>
    /// Adds every listener to <paramref name="into"/>, for a view that shows
    /// who listens. Listeners of one node and name come in the order they
    /// were added. The order of the nodes and names is none to show.
    /// </summary>
    public void CopyTo(List<ScriptListener> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        foreach (KeyValuePair<Port, Heard> port in _byPort)
        {
            List<ScriptPlace> by = port.Value.By;
            for (int i = 0; i < by.Count; i++)
                into.Add(new ScriptListener(port.Key.Node, port.Key.Name, by[i]));
        }
    }

    internal void NoteMissedFire(SceneNode node, string name) => _missed.Add(new Port(node.Id, name));

    // The list itself. Queueing an event runs no script, so it holds still
    // while a fire walks it.
    internal List<long>? Find(SceneNode node, string name) => Find(node.Id, name);

    // By the node's id, for a node that may have left the scene since.
    internal List<long>? Find(Guid node, string name) =>
        Count > 0 && _byPort.TryGetValue(new Port(node, name), out Heard? heard) ? heard.Listeners : null;

    // Who listens to a node and a name, in the order of Find's list. Null
    // when nobody does. A trace reads it inside the dispatch.
    internal List<ScriptPlace>? WhoHears(SceneNode node, string name) =>
        Count > 0 && _byPort.TryGetValue(new Port(node.Id, name), out Heard? heard) ? heard.By : null;

    internal void Clear()
    {
        _byPort.Clear();
        _missed.Clear();
        _toldNoTouch.Clear();
        Count = 0;
        TouchListenerCount = 0;
        Version++;
    }

    // The listeners of one node and name, and who each is. The two lists run
    // side by side.
    private sealed class Heard
    {
        public List<long> Listeners { get; } = [];

        public List<ScriptPlace> By { get; } = [];
    }

    private readonly record struct Port(Guid Node, string Name);
}
