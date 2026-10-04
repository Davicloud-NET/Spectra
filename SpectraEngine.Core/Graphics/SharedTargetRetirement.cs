using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Graphics;

// Tracks shared colour target generations. A shared target is rebuilt on resize
// under a new generation, since the consumer imported the old handle and may
// still be sampling it. A retired generation is held until the consumer
// acknowledges it. Render thread only.
internal sealed class SharedTargetRetirement
{
    // Past this many retired generations the oldest is released unacknowledged,
    // so a consumer that never calls back cannot leak a surface per resize.
    // Eight full-screen RGBA8 surfaces is about 66 MB at 1080p.
    internal const int Cap = 8;

    private readonly record struct Retired(int Generation, Action Release, Action? OfferTurn);

    private readonly ILogger _logger;
    private readonly List<Retired> _retired = [];
    private int _generation;

    internal SharedTargetRetirement(ILogger logger) => _logger = logger;

    // Zero before the first Next().
    internal int CurrentGeneration => _generation;

    internal int PendingCount => _retired.Count;

    // Generations released without an acknowledgement because Cap was reached.
    internal int ForcedReleaseCount { get; private set; }

    // Generation numbers are never reused; the consumer re-imports on a change.
    internal int Next() => ++_generation;

    // Holds release until the consumer acknowledges the generation, or Cap forces
    // it. release runs once. offerTurn is called by OfferTurns every frame until then.
    internal void Retire(int generation, Action release, Action? offerTurn = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        _retired.Add(new Retired(generation, release, offerTurn));

        while (_retired.Count > Cap)
        {
            Retired oldest = _retired[0];
            _retired.RemoveAt(0);
            ForcedReleaseCount++;

            // Warn once; a resize drag would repeat it per step.
            if (ForcedReleaseCount == 1)
            {
                _logger.LogWarning(
                    "Shared target generation {Generation} was released without the consumer acknowledging it: " +
                    "{Cap} retired generations were already held. Either nothing is consuming the shared target " +
                    "or it never calls back, and holding them all would leak a full-screen surface per resize.",
                    oldest.Generation, Cap);
            }
            else
            {
                _logger.LogDebug(
                    "Shared target generation {Generation} force-released ({Count} so far).",
                    oldest.Generation, ForcedReleaseCount);
            }

            oldest.Release();
        }
    }

    // Offers every retired generation's key to the consumer. The consumer keeps
    // more than one turn queued at the compositor, and the producer hands the key
    // over only once before retiring a target. Without this the second queued
    // turn waits forever on the consumer's render thread and the UI freezes.
    internal void OfferTurns()
    {
        for (int i = 0; i < _retired.Count; i++)
            _retired[i].OfferTurn?.Invoke();
    }

    // Releases this generation and every older one; returns the count. Older
    // ones too, because a consumer can skip a generation it never imported.
    internal int ConsumerReleased(int generation)
    {
        int released = 0;

        // Retire only appends, so the list is ordered by generation.
        while (_retired.Count > 0 && _retired[0].Generation <= generation)
        {
            Retired oldest = _retired[0];
            _retired.RemoveAt(0);
            released++;
            oldest.Release();
        }

        return released;
    }

    // For shutdown: releases everything, acknowledged or not.
    internal void ReleaseAll()
    {
        // Copy first: a release callback may re-enter.
        Retired[] pending = [.. _retired];
        _retired.Clear();
        foreach (Retired entry in pending)
            entry.Release();
    }
}
