using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// The engine's caption view until it has one that draws: each caption is
/// written to the log once, when it appears. It only reads the feed, as any
/// other view does.
/// </summary>
public sealed class CaptionLogView
{
    private readonly ILogger _logger;

    // The id of the newest caption written.
    private long _written;

    /// <param name="logger">Where the captions are written, at Information.</param>
    public CaptionLogView(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Writes the captions that have appeared since the last call. Call once a frame.</summary>
    public void Update(CaptionFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        if (feed.LastId == _written)
            return;

        IReadOnlyList<Caption> captions = feed.Captions;
        for (int i = 0; i < captions.Count; i++)
        {
            if (captions[i].Id > _written)
                Write(captions[i]);
        }

        _written = feed.LastId;
    }

    private void Write(in Caption caption)
    {
        // One line in the log, whatever the writer's line breaks.
        string words = caption.Text.Replace('\n', ' ');

        if (caption.Kind == CaptionKind.Sound)
            _logger.LogInformation("Caption: [{Words}]", words);
        else if (caption.Speaker is { } speaker)
            _logger.LogInformation("Caption: {Speaker}: {Words}", speaker, words);
        else
            _logger.LogInformation("Caption: {Words}", words);
    }
}
