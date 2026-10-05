using Spectra.Kitchen.Audio;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Audio;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Spectra.Kitchen.Rules;

// The markers of one sound, for AudioRule: taken from the WAV's cue points or
// the label file beside it, named, checked against the end and moved to the
// project rate.
internal static class AudioMarkerCook
{
    public static AudioMarker[] Run(IRuleContext context, DecodedAudio decoded, int targetRate, long cookedFrames)
    {
        IReadOnlyList<SourceMarker> declared = Declared(context, decoded);
        if (declared.Count == 0) return [];

        // Time order gives an unnamed marker its number. At one frame the
        // unnamed sort first, so the number does not depend on the order the
        // source listed them in.
        SourceMarker[] ordered =
        [
            .. declared
                .OrderBy(static marker => marker.Frame)
                .ThenBy(static marker => marker.Label, StringComparer.Ordinal),
        ];

        var cooked = new List<AudioMarker>(ordered.Length);
        for (int i = 0; i < ordered.Length; i++)
        {
            long sourceFrame = ordered[i].Frame;
            string at = Seconds(sourceFrame, decoded.SampleRate);
            string name = NameOf(context, ordered, i, at);

            if (sourceFrame > decoded.FrameCount)
            {
                string end = Seconds(decoded.FrameCount, decoded.SampleRate);

                context.Report(CookDiagnostic.Warning(
                    CookDiagnosticCodes.AudioMarkerPastEnd,
                    $"'{context.SourcePath}' has the marker '{name}' at {at} s and the sound ends at {end} s, " +
                    "so the marker is dropped. Move it inside the sound.",
                    context.SourcePath));

                continue;
            }

            // The integer conversion the length and the loop use, clamped for
            // the reason the loop end is.
            long frame = Math.Min(
                AudioResampler.ConvertFrames(sourceFrame, decoded.SampleRate, targetRate), cookedFrames);

            cooked.Add(new AudioMarker(frame, name));
        }

        return [.. cooked];
    }

    // The WAV's own cue points win. The label file beside it is for editors
    // that write none. Frames are at the WAV's rate either way.
    private static IReadOnlyList<SourceMarker> Declared(IRuleContext context, DecodedAudio decoded)
    {
        // Probed for every sound, so adding the file later cooks the sound
        // again. Read throws on a miss.
        string labels = MarkerLabelFile.PathFor(context.SourcePath);
        if (!context.Probe(labels)) return decoded.Markers;

        if (decoded.Markers.Count > 0)
        {
            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.AudioMarkerLabelFileUnused,
                $"'{labels}' is not read: '{context.SourcePath}' has cue points of its own, and those are " +
                "its markers. Delete the label file, or save the sound without cue points.",
                labels));

            return decoded.Markers;
        }

        var unreadable = new List<int>();
        SourceMarker[] markers = MarkerLabelFile.Read(context.Read(labels), decoded.SampleRate, unreadable);

        foreach (int line in unreadable)
        {
            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.AudioMarkerLabelUnreadable,
                $"Line {line} of '{labels}' is not a marker, so it is skipped. A marker is a time in seconds, a " +
                "tab and a name. An end time and a tab may sit between the two, the way Audacity exports a " +
                "label track.",
                labels,
                line));
        }

        return markers;
    }

    // The marker's own name, or the prefix and its one-based place in time.
    private static string NameOf(IRuleContext context, SourceMarker[] ordered, int index, string at)
    {
        if (ordered[index].Label.Length > 0) return ordered[index].Label;

        string name = AudioRule.UnnamedMarkerPrefix + (index + 1).ToString(CultureInfo.InvariantCulture);

        foreach (SourceMarker other in ordered)
        {
            if (!string.Equals(other.Label, name, StringComparison.Ordinal)) continue;

            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.AudioMarkerNameTaken,
                $"'{context.SourcePath}' has a marker with no name at {at} s, which the cook calls '{name}', " +
                $"and another marker that is named '{name}' in the file. Whatever waits for '{name}' gets " +
                "both. Give the unnamed marker a name.",
                context.SourcePath));

            break;
        }

        return name;
    }

    private static string Seconds(long frames, int sampleRate) =>
        ((double)frames / sampleRate).ToString("0.###", CultureInfo.InvariantCulture);
}
