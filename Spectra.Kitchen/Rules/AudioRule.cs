using Spectra.Kitchen.Audio;
using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Audio;
using System;
using System.IO;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Turns an authored WAV into a <c>.saudio</c>: PCM16 at the project sample rate,
/// with loop points and markers in sample frames. The source file is not also
/// copied into the pack.
/// </summary>
public sealed class AudioRule : IRule
{
    // Must match what WaveDecoder reads.
    internal static readonly string[] SourceExtensions = [".wav", ".wave"];

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

        if (!TryDecode(context, out DecodedAudio decoded)) return;

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
        if (decoded.Channels == 2 && !flat) ReportStereoPositional(context);

        long frames = samples.Length / decoded.Channels;
        int framesPerSeekEntry = frames > StreamingThresholdSeconds * targetRate
            ? Math.Max(1, (int)(SecondsPerSeekEntry * targetRate))
            : 0;

        AudioMarker[] markers = AudioMarkerCook.Run(context, decoded, targetRate, frames);

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

    // False when the file could not be decoded, which has been reported.
    private static bool TryDecode(IRuleContext context, out DecodedAudio decoded)
    {
        byte[] source = context.Read(context.SourcePath);

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

            decoded = default;
            return false;
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

        return true;
    }

    private static void ReportStereoPositional(IRuleContext context)
    {
        context.Report(CookDiagnostic.Warning(
            CookDiagnosticCodes.AudioStereoPositional,
            $"'{context.SourcePath}' is stereo, so OpenAL will play it unpositioned however it is placed - " +
            "at full level, wherever the listener stands, with nothing reporting it. Export it mono if it " +
            $"is meant to be a sound in the world, or end its name '{FlatSuffix}' to say it is meant to be " +
            "flat.",
            context.SourcePath));
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
}
