using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The scripts of a running level that will do nothing more, by their nodes:
/// the ones their host switched off. The script runtime adds them, and a view
/// of what scripts send reads them. Render thread only.
/// </summary>
public sealed class StoppedScripts
{
    private readonly HashSet<Guid> _nodes = [];

    internal StoppedScripts()
    {
    }

    /// <summary>How many there are. It only goes up while a level runs.</summary>
    public int Count => _nodes.Count;

    /// <summary>Notes that the script on a node was switched off. Once is enough.</summary>
    public void Add(Guid script) => _nodes.Add(script);

    /// <summary>Whether the script on a node was switched off.</summary>
    public bool Contains(Guid script) => _nodes.Contains(script);

    internal void Clear() => _nodes.Clear();
}
