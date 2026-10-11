using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Entities;

// The moves of nodes while a level plays: which are refused and why, and
// the authored transform every moved node goes back to at Stop.
internal sealed class PlayMoves
{
    private readonly ILogger _logger;
    private readonly MovedNodeJournal _moved = new();

    // Log a refused move once per node, not once per tick.
    private readonly HashSet<Guid> _refusedNodes = [];

    public PlayMoves(ILogger logger) => _logger = logger;

    public int MovedCount => _moved.Count;

    public int RefusedCount { get; private set; }

    public void Begin()
    {
        RefusedCount = 0;
        _refusedNodes.Clear();
    }

    public void PutBack() => _moved.Restore();

    // The move for a caller that reports a refusal itself. Nothing is logged.
    public bool TryMove(SceneNode node, in Transform value, [NotNullWhen(false)] out string? refusal)
    {
        refusal = FindRefusal(node, in value);
        if (refusal is not null)
        {
            RefusedCount++;
            return false;
        }

        _moved.Record(node);
        node.LocalTransform = value;
        return true;
    }

    public bool Move(SceneNode node, in Transform value)
    {
        if (TryMove(node, in value, out string? refusal))
            return true;

        if (_refusedNodes.Add(node.Id))
            _logger.LogError("An entity tried to move '{NodeName}' and was refused: {Why}.", node.Name, refusal);

        return false;
    }

    private static string? FindRefusal(SceneNode node, in Transform value)
    {
        // A moving world brush recompiles the level every tick, and the
        // picture stays right while it does.
        if (node.SubtreeStaticWorldBrushCount > 0)
        {
            return "a world brush sits at or below it, and only parts can move while a level plays. " +
                "Convert the brush to a part";
        }

        if (node.SubtreeBrushCount > 0 && value.Scale != node.LocalScale)
        {
            return "the move changes its scale, and a node cannot scale a brush at or below it. " +
                "Resize the brush instead";
        }

        return null;
    }
}
