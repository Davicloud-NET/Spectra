using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The Sends section: the wires leaving the selected entity. Present only
/// when one entity is selected. UI thread only.
/// </summary>
// After an edit, stale snapshots are ignored until the engine echoes it, or
// until HoldSnapshots have passed and the engine's list wins (an edit can be
// refused, for example during play mode).
public sealed class EntityWiringModel : ObservableObject
{
    /// <summary>
    /// How many disagreeing snapshots to ignore before the engine wins.
    /// </summary>
    public const int HoldSnapshots = 6;

    private readonly Action<Guid, IReadOnlyList<EntityConnection>> _apply;
    private EntityConnection[]? _pending;
    private int _ticks;
    private Guid _nodeId;
    private bool _hasEntity;
    private bool _isKnown = true;
    private IReadOnlyList<string> _outputs = [];
    private EntitySchema? _sender;
    private IReadOnlyList<EntityTargetInfo> _targets = [];
    private bool _targetsTruncated;

    /// <summary>Every entity a wire could aim at, for the picker.</summary>
    public IReadOnlyList<EntityTargetInfo> Targets => _targets;

    /// <summary>Whether the scene has more than the list carries.</summary>
    public bool TargetsTruncated => _targetsTruncated;

    /// <summary>What to say about a capped list, or empty.</summary>
    public string TargetsNote => _targetsTruncated
        ? $"This scene has more than {EntityPanelInfo.MaxTargets} entities; the picker shows the first of them."
        : string.Empty;

    /// <summary>The session's schema catalogue, assigned by the owning panel.</summary>
    public EntitySchemaCatalog? Schemas { get; set; }

    internal EntityWiringModel(Action<Guid, IReadOnlyList<EntityConnection>> apply) => _apply = apply;

    /// <summary>The wires, in authored order.</summary>
    public ObservableCollection<ConnectionRowModel> Rows { get; } = [];

    /// <summary>Whether the selection is one node carrying an entity.</summary>
    public bool HasEntity
    {
        get => _hasEntity;
        private set
        {
            if (Set(ref _hasEntity, value))
                Raise(nameof(IsEmpty));
        }
    }

    /// <summary>Whether there is an entity here and it has no wires yet.</summary>
    public bool IsEmpty => _hasEntity && Rows.Count == 0;

    /// <summary>
    /// Whether this session has a schema for the selected class, so the
    /// section can say why the output dropdowns are text boxes.
    /// </summary>
    public bool IsKnown
    {
        get => _isKnown;
        private set
        {
            if (Set(ref _isKnown, value))
                Raise(nameof(ShowsUnknownOutputs));
        }
    }

    /// <summary>Whether to explain that the outputs could not be listed.</summary>
    public bool ShowsUnknownOutputs => _hasEntity && !_isKnown;

    /// <summary>Takes one published snapshot's entity payload.</summary>
    public void Apply(EntityPanelInfo? info)
    {
        if (info is null)
        {
            _pending = null;
            _ticks = 0;
            _nodeId = Guid.Empty;
            HasEntity = false;
            IsKnown = true;
            if (Rows.Count > 0)
            {
                Rows.Clear();
                Raise(nameof(IsEmpty));
            }

            return;
        }

        // A pending edit belongs to the previous node.
        if (info.NodeId != _nodeId)
        {
            _nodeId = info.NodeId;
            _pending = null;
            _ticks = 0;
        }

        HasEntity = true;
        IsKnown = info.IsKnown;
        _outputs = info.Outputs;
        _sender = null;
        Schemas?.TryGetSchema(info.ClassName, out _sender);
        _targets = info.Targets;
        _targetsTruncated = info.TargetsTruncated;

        if (_pending is not null)
        {
            if (Matches(info.Connections, _pending))
            {
                _pending = null;
                _ticks = 0;
            }
            else if (++_ticks < HoldSnapshots)
            {
                // Snapshot predates the edit.
                return;
            }
            else
            {
                _pending = null;
                _ticks = 0;
            }
        }

        SyncRows(info.Connections);
    }

    /// <summary>Adds an empty wire and posts the new list.</summary>
    public void Add()
    {
        if (!_hasEntity)
            return;

        var row = new ConnectionRowModel(Post) { Sender = _sender };
        row.Seed(_outputs, _targets, _targetsTruncated, Schemas);
        Rows.Add(row);
        Raise(nameof(IsEmpty));
        Post();
    }

    /// <summary>Removes one wire and posts the new list.</summary>
    public void Remove(ConnectionRowModel row)
    {
        if (row is null || !Rows.Remove(row))
            return;

        Raise(nameof(IsEmpty));
        Post();
    }

    // Connection order is authored data in map.json: never sort or de-duplicate.
    private void Post()
    {
        if (!_hasEntity)
            return;

        var wires = new EntityConnection[Rows.Count];
        for (int i = 0; i < wires.Length; i++)
            wires[i] = Rows[i].ToConnection();

        _pending = wires;
        _ticks = 0;
        _apply(_nodeId, wires);
    }

    // Patch in place: a fresh collection per publish resets scroll and drops
    // a half-typed value.
    private void SyncRows(IReadOnlyList<EntityConnectionInfo> wires)
    {
        bool countChanged = Rows.Count != wires.Count;

        while (Rows.Count > wires.Count)
            Rows.RemoveAt(Rows.Count - 1);

        while (Rows.Count < wires.Count)
            Rows.Add(new ConnectionRowModel(Post));

        for (int i = 0; i < wires.Count; i++)
        {
            Rows[i].Sender = _sender;
            Rows[i].Refresh(wires[i], _outputs, _targets, _targetsTruncated, Schemas);
        }

        if (countChanged)
            Raise(nameof(IsEmpty));
    }

    private static bool Matches(IReadOnlyList<EntityConnectionInfo> reported, EntityConnection[] wanted)
    {
        if (reported.Count != wanted.Length)
            return false;

        for (int i = 0; i < wanted.Length; i++)
        {
            if (reported[i].Wire != wanted[i])
                return false;
        }

        return true;
    }
}
