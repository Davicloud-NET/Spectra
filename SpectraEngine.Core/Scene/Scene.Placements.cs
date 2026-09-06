using System;
using System.Collections.Generic;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Scene;

public sealed partial class Scene
{
    private readonly PlacementJournal _placementJournal = new();
    private readonly HashSet<SceneNode> _placementMembers = [];
    private readonly HashSet<SceneNode> _pendingPlacementNodes = [];
    private bool _placementsInitialized;
    private long _footprintSnapshotId;

    // Internal structural mutations carry node information through membership
    // callbacks. The public context-free dirty mark still requests a full audit.
    internal void MarkStructuralWorldDirty()
    {
        if (RefuseForCompiledWorld("A static-world dirty mark", isRebuild: false)) return;
        _staticWorldVersion++;
    }

    private void TrackWorldPlacement(SceneNode node)
    {
        if (_compiledStaticWorld is not null) return;
        if (!ReferenceEquals(node.Owner, this) || !node.IsStaticWorldBrush)
        { ForgetWorldPlacement(node); return; }
        bool added = _placementMembers.Add(node);
        _pendingPlacementNodes.Add(node);
        if (_placementsInitialized)
            _placementJournal.Set(node.PlacementIdentity, new(node.Brush!, node.WorldMatrix),
                added ? PreviousWorldPlacement(node) : null, reorder: added);
    }

    private void ForgetWorldPlacement(SceneNode node)
    {
        _placementMembers.Remove(node);
        _pendingPlacementNodes.Remove(node);
        _dirtyBrushSubtrees.Remove(node);
        _placementJournal.Remove(node.PlacementIdentity);
    }

    private void ReorderWorldPlacements(SceneNode root)
    {
        if (!_placementsInitialized || root.SubtreeStaticWorldBrushCount == 0) return;
        foreach (var node in root.Traverse())
        {
            if (!node.IsStaticWorldBrush) continue;
            _placementJournal.Set(node.PlacementIdentity, new(node.Brush!, node.WorldMatrix),
                PreviousWorldPlacement(node), reorder: true);
            _pendingPlacementNodes.Add(node);
        }
    }

    private Guid? PreviousWorldPlacement(SceneNode node)
    {
        for (SceneNode? current = node; current?.Parent is { } parent; current = parent)
        {
            for (var sibling = current.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
            {
                if (sibling.SubtreeStaticWorldBrushCount == 0) continue;
                var last = sibling;
                while (true)
                {
                    SceneNode? childWithBrush = null;
                    for (int i = last.Children.Count - 1; i >= 0; i--)
                        if (last.Children[i].SubtreeStaticWorldBrushCount > 0)
                        { childWithBrush = last.Children[i]; break; }
                    if (childWithBrush is null) return last.PlacementIdentity;
                    last = childWithBrush;
                }
            }
            if (parent.IsStaticWorldBrush) return parent.PlacementIdentity;
        }
        return null;
    }

    private PlacementSnapshot? SnapshotBrushPlacements(out string? defectMessage)
    {
        defectMessage = null;
        if (_snapshotForceFull || !_placementsInitialized)
        {
            Guid? prior = null;
            foreach (var node in Nodes)
            {
                if (!node.IsStaticWorldBrush) continue;
                var world = node.WorldMatrix;
                if (DescribeNonRigidDefect(world) is { } defect)
                { defectMessage = DescribeBrushNodeDefect(node, defect); return null; }
                _placementJournal.Set(node.PlacementIdentity, new(node.Brush!, world), prior, reorder: true);
                prior = node.PlacementIdentity;
            }
        }
        else
        {
            foreach (var root in _dirtyBrushSubtrees)
            {
                if (!ReferenceEquals(root.Owner, this)) continue;
                foreach (var node in root.Traverse())
                    if (node.IsStaticWorldBrush) _pendingPlacementNodes.Add(node);
            }
            foreach (var node in _pendingPlacementNodes)
            {
                var world = node.WorldMatrix;
                if (DescribeNonRigidDefect(world) is { } defect)
                { defectMessage = DescribeBrushNodeDefect(node, defect); return null; }
                _placementJournal.Set(node.PlacementIdentity, new(node.Brush!, world));
            }
        }
        _placementsInitialized = true;
        _snapshotForceFull = false;
        _pendingPlacementNodes.Clear();
        _dirtyBrushSubtrees.Clear();
        return _placementJournal.Capture();
    }

    private ChunkCoord[] CollectDirtyCells(PlacementSnapshot snapshot)
    {
        if (_footprintSnapshotId != snapshot.Id)
        {
            foreach (var change in snapshot.Changes)
            {
                if (change.Before is { } before) MarkCellsDirty(ChunkGrid.ComputeFootprint(before.Placement));
                if (change.After is { } after) MarkCellsDirty(ChunkGrid.ComputeFootprint(after.Placement));
            }
            _footprintSnapshotId = snapshot.Id;
        }
        return DrainPendingDirtyCells();
    }
}
