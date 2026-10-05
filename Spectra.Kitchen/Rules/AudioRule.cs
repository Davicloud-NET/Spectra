using Spectra.Kitchen.Audio;
using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Audio;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Turns an authored WAV into a <c>.saudio</c>: PCM16 at the project sample rate,
/// with loop points and markers in sample frames. The source file is not also
/// copied into the pack.
/// </summary>
public sealed class AudioRule : IRule
{
    // Must match what WaveDecoder reads.
    private static readonly string[] SourceExtensions = [".wav", ".wave"];

    /// <summary>
    /// Suffix on a file's stem that marks a sound as non-positional. Matched
    /// case-insensitively.
    /// </summary>
    public const string FlatSuffix = "_2d";

    /// <summary>Sounds longer than this are flagged streaming and get a seek table.</summary>
    public const double StreamingThresholdSeconds = 10.0;

    /// <summary>Seconds of audio between two seek points in a streamed sound.</summary>
    public const double SecondsPerSeekEntry = 1.0;

    /// <summary>
    /// What a marker with no name is called, followed by its number: the
    /// markers of a sound are counted from 1 in time order.
    /// </summary>
    public const string UnnamedMarkerPrefix = "marker";

    /// <inheritdoc/>
    public RuleKind Kind => RuleKind.Audio;

    /// <inheritdoc/>
    public int Version => 2;

    /// <inheritdoc/>
    // Not the profile: every profile produces the same bytes for a sound.
    public CookSettingKeys SettingsRead => CookSettingKeys.AudioSampleRate;

    /// <summary>Whether <paramref name="contentPath"/> is a sound this rule cooks.</summary>
    public static bool Handles(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        foreach (string extension in SourceExtensions)
        {
            if (contentPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>Whether the file's stem ends with <see cref="FlatSuffix"/>.</summary>
    public static bool IsDeclaredFlat(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);

        string stem = Path.GetFileNameWithoutExtension(contentPath);
        return stem.EndsWith(FlatSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public void Cook(IRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        byte[] source = context.Read(context.SourcePath);

        DecodedAudio decoded;
        try
        {
            decoded = WaveDecoder.Decode(source, context.SourcePath);
        }
        catch (InvalidDataException ex)
        {
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.AudioUndecodable,
                $"'{context.SourcePath}' could not be decoded: {ex.Message}",
                context.SourcePath));

            return;
        }

        if (decoded.LoopWasRefused)
        {
            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.AudioLoopUnusable,
                $"'{context.SourcePath}' declares a loop this engine cannot play - a region outside its own " +
                "data, an empty one, or an alternating or backward loop - so the cooked sound plays once. " +
                "Only forward loops inside the sound are carried.",
                context.SourcePath));
        }

        int targetRate = context.AudioSampleRate;
        short[] samples = decoded.Samples;
        LoopRegion loop = decoded.Loop;

        if (decoded.SampleRate != targetRate)
        {
            samples = AudioResampler.Resample(samples, decoded.Channels, decoded.SampleRate, targetRate);

            // Same integer conversion as the length. Going through seconds or
            // floats lands a frame off, which clicks at the loop boundary.
            loop = ConvertLoop(loop, decoded.SampleRate, targetRate, samples.Length / decoded.Channels);

            context.Report(CookDiagnostic.Info(
                CookDiagnosticCodes.AudioResampled,
                $"'{context.SourcePath}' was resampled from {decoded.SampleRate} Hz to the project's " +
                $"{targetRate} Hz. The runtime never resamples, so this happens once, here.",
                context.SourcePath));
        }

        bool flat = IsDeclaredFlat(context.SourcePath);
        if (decoded.Channels == 2 && !flat)
        {
            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.AudioStereoPositional,
                $"'{context.SourcePath}' is stereo, so OpenAL will play it unpositioned however it is placed - " +
                "at full level, wherever the listener stands, with nothing reporting it. Export it mono if it " +
                $"is meant to be a sound in the world, or end its name '{FlatSuffix}' to say it is meant to be " +
                "flat.",
                context.SourcePath));
        }

        long frames = samples.Length / decoded.Channels;
        int framesPerSeekEntry = frames > StreamingThresholdSeconds * targetRate
            ? Math.Max(1, (int)(SecondsPerSeekEntry * targetRate))
            : 0;

        AudioMarker[] markers = CookMarkers(context, decoded, targetRate, frames);

        byte[] cooked;
        try
        {
            cooked = SaudioWriter.Write(
                new AudioFormat(targetRate, decoded.Channels),
                samples,
                loop,
                positional: !flat,
                framesPerSeekEntry,
                markers);
        }
        catch (ArgumentException ex)
        {
            // Report, don't throw, so the diagnostic names the asset.
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.AudioEncodeFailed,
                $"'{context.SourcePath}' produced a sound the container cannot hold: {ex.Message}",
                context.SourcePath));

            return;
        }

        context.Emit(AudioContentPath.CookedPathFor(context.SourcePath), cooked, PackEntryKind.Audio);
    }

    // The end is clamped to the resampled length: independent rounding can put it
    // one frame past, and the reader refuses a loop that ends past the sound.
    private static LoopRegion ConvertLoop(LoopRegion loop, int fromRate, int toRate, long frames)
    {
        if (!loop.IsLooping) return LoopRegion.None;

        long start = AudioResampler.ConvertFrames(loop.StartFrame, fromRate, toRate);
        long end = Math.Min(AudioResampler.ConvertFrames(loop.EndFrame, fromRate, toRate), frames);

        // A loop under one frame at the new rate is dropped.
        return end > start ? new LoopRegion(start, end) : LoopRegion.None;
    }

    // Names the unnamed, drops what lies past the end and moves the rest to
    // the project rate.
    private static AudioMarker[] CookMarkers(
        IRuleContext context, DecodedAudio decoded, int targetRate, long cookedFrames)
    {
        IReadOnlyList<SourceMarker> declared = DeclaredMarkers(context, decoded);
        if (declared.Count == 0) return [];

        // Time order gives an unnamed marker its number. OrderBy is stable, so
        // two at one frame keep the order the source listed them in.
        SourceMarker[] ordered = declared.OrderBy(static marker => marker.Frame).ToArray();

        var cooked = new List<AudioMarker>(ordered.Length);
        for (int i = 0; i < ordered.Length; i++)
        {
            long sourceFrame = ordered[i].Frame;
            string name = ordered[i].Label.Length > 0
                ? ordered[i].Label
                : UnnamedMarkerPrefix + (i + 1).ToString(CultureInfo.InvariantCulture);

            if (sourceFrame > decoded.FrameCount)
            {
                string at = Seconds(sourceFrame, decoded.SampleRate);
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

        return cooked.ToArray();
    }

    // The WAV's own cue points win. The label file beside it is for editors
    // that write none. Frames are at the WAV's rate either way.
    private static IReadOnlyList<SourceMarker> DeclaredMarkers(IRuleContext context, DecodedAudio decoded)
    {
        if (decoded.Markers.Count > 0) return decoded.Markers;

        // Probe first: most sounds have no label file, and Read throws on a miss.
        string labels = MarkerLabelFile.PathFor(context.SourcePath);
        if (!context.Probe(labels)) return [];

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

    private static string Seconds(long frames, int sampleRate) =>
        ((double)frames / sampleRate).ToString("0.###", CultureInfo.InvariantCulture);
}
