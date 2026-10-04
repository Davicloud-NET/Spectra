using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Hosting;

/// <summary>
/// Accumulates the scene's structural events between snapshots, so a shell's
/// tree view can be updated incrementally instead of rebuilt. Render thread only.
/// </summary>
// Transform changes are not logged: they fire per moved node per frame, and
// an inspector reads current values from the snapshot instead.
public sealed class SceneChangeLog
{
    /// <summary>
    /// How many changes may pile up before the log stops recording and reports
    /// an overflow instead.
    /// </summary>
    public const int DefaultCapacity = 4096;

    private readonly int _capacity;
    private List<SceneChange> _changes = [];
    private Scene.Scene? _scene;

    /// <summary>Creates a log with the default capacity.</summary>
    public SceneChangeLog()
        : this(DefaultCapacity)
    {
    }

    /// <summary>Creates a log that overflows after <paramref name="capacity"/> changes.</summary>
    public SceneChangeLog(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>
    /// True when more changes arrived than the log could hold, so the batch is
    /// incomplete and a consumer must rebuild rather than replay. Cleared by
    /// <see cref="Drain"/> along with the batch.
    /// </summary>
    public bool Overflowed { get; private set; }

    /// <summary>How many changes are waiting in the current batch.</summary>
    public int Count => _changes.Count;

    /// <summary>
    /// Starts recording <paramref name="scene"/>'s structural events, detaching
    /// from whichever scene was previously observed. Passing null detaches.
    /// A swap is reported as an overflow, so the consumer rebuilds.
    /// </summary>
    public void Observe(Scene.Scene? scene)
    {
        if (ReferenceEquals(_scene, scene))
            return;

        if (_scene is { } previous)
        {
            previous.NodeAdded -= OnNodeAdded;
            previous.NodeRemoved -= OnNodeRemoved;
            previous.NodeReparented -= OnNodeReparented;
            previous.NodeRenamed -= OnNodeRenamed;
        }

        _scene = scene;

        if (scene is not null)
        {
            scene.NodeAdded += OnNodeAdded;
            scene.NodeRemoved += OnNodeRemoved;
            scene.NodeReparented += OnNodeReparented;
            scene.NodeRenamed += OnNodeRenamed;
        }

        _changes.Clear();
        Overflowed = true;
    }

    /// <summary>
    /// Hands the accumulated batch to the caller and starts a fresh one,
    /// reporting whether the batch overflowed.
    /// </summary>
    public (IReadOnlyList<SceneChange> Changes, bool Overflowed) Drain()
    {
        bool overflowed = Overflowed;
        Overflowed = false;

        if (_changes.Count == 0)
            return (Array.Empty<SceneChange>(), overflowed);

        // Hand the list over and allocate a new one: reusing it would change
        // a snapshot's contents under a UI thread still reading it.
        List<SceneChange> batch = _changes;
        _changes = new List<SceneChange>(Math.Min(batch.Count, _capacity));
        return (batch, overflowed);
    }

    private void OnNodeAdded(SceneNode node) => Record(SceneChangeKind.Added, node);

    private void OnNodeRemoved(SceneNode node) => Record(SceneChangeKind.Removed, node);

    private void OnNodeReparented(SceneNode node) => Record(SceneChangeKind.Reparented, node);

    private void OnNodeRenamed(SceneNode node) => Record(SceneChangeKind.Renamed, node);

    private void Record(SceneChangeKind kind, SceneNode node)
    {
        if (_changes.Count >= _capacity)
        {
            // Bounded, so a stalled UI thread cannot grow this without limit.
            Overflowed = true;
            return;
        }

        _changes.Add(new SceneChange(
            kind,
            node.Id,
            node.Parent?.Id ?? Guid.Empty,
            node.Name,
            node.IndexInParent,
            SceneNodeClassifier.Classify(node)));
    }
}
