using SpectraEngine.Core.Inspection;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The Receives section: the wires that arrive at the selected entity. Read
/// only, since a wire is edited on the entity that sends it. UI thread only.
/// </summary>
public sealed class EntityArrivalsModel : ObservableObject
{
    private IReadOnlyList<EntityIncomingInfo> _shown = [];
    private bool _hasEntity;
    private bool _isTruncated;

    /// <summary>The arriving wires, in the order their senders joined the scene.</summary>
    public ObservableCollection<EntityArrivalRow> Rows { get; } = [];

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

    /// <summary>Whether there is an entity here and nothing is wired to it.</summary>
    public bool IsEmpty => _hasEntity && Rows.Count == 0;

    /// <summary>Whether there is at least one arriving wire.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Whether more wires arrive than the list carries.</summary>
    public bool IsTruncated
    {
        get => _isTruncated;
        private set => Set(ref _isTruncated, value);
    }

    /// <summary>What to say about a capped list.</summary>
    public string TruncatedNote =>
        $"More than {EntityPanelInfo.MaxIncoming} wires arrive here. These are the first of them.";

    /// <summary>Takes one published snapshot's entity payload.</summary>
    public void Apply(EntityPanelInfo? info)
    {
        HasEntity = info is not null;
        IsTruncated = info?.IncomingTruncated ?? false;

        IReadOnlyList<EntityIncomingInfo> arriving = info?.Incoming ?? [];
        if (Same(arriving, _shown))
            return;

        // Rebuilt only on a change: a fresh list per publish would drop the
        // pointer's hover on every snapshot.
        _shown = arriving;
        Rows.Clear();
        foreach (EntityIncomingInfo wire in arriving)
            Rows.Add(new EntityArrivalRow(wire.SourceId, wire.SourceName, wire.Output, wire.Input));

        Raise(nameof(IsEmpty));
        Raise(nameof(HasRows));
    }

    private static bool Same(IReadOnlyList<EntityIncomingInfo> a, IReadOnlyList<EntityIncomingInfo> b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }
}
