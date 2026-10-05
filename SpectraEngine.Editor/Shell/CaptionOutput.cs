using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Hosting;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Writes each caption the engine shows into the Output panel once, when it
/// appears. It stands in for a caption view until the game UI draws one.
/// UI thread only.
/// </summary>
// It reads the feed in the snapshot, as any view does, and not the engine's
// log: the log relay carries warnings and errors only.
public sealed class CaptionOutput
{
    private readonly OutputLog _output;

    // The list last read. The feed hands out the same one until something in it changes.
    private IReadOnlyList<Caption>? _read;

    // The id of the newest caption written. Ids only go up while an engine runs.
    private long _written;

    /// <param name="output">Where the captions are written.</param>
    public CaptionOutput(OutputLog output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>Takes one published snapshot.</summary>
    public void Apply(FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        IReadOnlyList<Caption> captions = snapshot.Captions;
        if (ReferenceEquals(captions, _read))
            return;

        _read = captions;

        long newest = _written;
        for (int i = 0; i < captions.Count; i++)
        {
            if (captions[i].Id <= _written)
                continue;

            _output.Append(OutputSeverity.Info, Line(captions[i]));
            newest = Math.Max(newest, captions[i].Id);
        }

        _written = newest;
    }

    /// <summary>
    /// Forgets what was written, when the session ends. The next engine
    /// counts its captions from the start.
    /// </summary>
    public void Reset()
    {
        _read = null;
        _written = 0;
    }

    /// <summary>One caption as a line, in the words the engine's own log uses.</summary>
    public static string Line(in Caption caption)
    {
        // One line, whatever the writer's line breaks.
        string words = caption.Text.Replace('\n', ' ');

        if (caption.Kind == CaptionKind.Sound)
            return $"Caption: [{words}]";

        return caption.Speaker is { } speaker ? $"Caption: {speaker}: {words}" : $"Caption: {words}";
    }
}
