using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The scripts of a running level that will do nothing more, by their nodes:
/// the ones their host switched off, and the ones whose node left the level.
/// The script runtime adds them, and a view of what scripts send reads them.
/// Render thread only.
/// </summary>
public sealed class StoppedScripts
{
    private readonly HashSet<Guid> _nodes = [];

    internal StoppedScripts()
    {
    }

    /// <summary>How many there are.</summary>
    public int Count => _nodes.Count;

    /// <summary>
    /// Goes up each time a script is added or runs again. A view that has
    /// read the scripts reads it to tell whether what it read still holds.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>Notes that the script on a node was switched off. Once is enough.</summary>
    public void Add(Guid script)
    {
        if (_nodes.Add(script))
            Version++;
    }

    /// <summary>Notes that a script runs on the node again, as one does whose node came back into the level.</summary>
    public void Remove(Guid script)
    {
        if (_nodes.Remove(script))
            Version++;
    }

    /// <summary>Whether the script on a node was switched off.</summary>
    public bool Contains(Guid script) => _nodes.Contains(script);

    internal void Clear()
    {
        _nodes.Clear();
        Version++;
    }
}
