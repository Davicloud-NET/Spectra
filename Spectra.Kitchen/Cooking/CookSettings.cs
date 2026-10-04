using Spectra.Kitchen.Cache;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;

namespace Spectra.Kitchen.Cooking;

/// <summary>
/// Everything one cook run was asked for.
/// </summary>
public sealed class CookSettings
{
    /// <summary>Where output goes. Defaults to the project's <c>cooked/</c> folder.</summary>
    public string? OutputPath { get; init; }

    /// <summary>What the cook is for.</summary>
    public CookProfile Profile { get; init; } = CookProfile.Ship;

    /// <summary>
    /// The backends a cook targets when nobody names any: the three with a
    /// working code generator. Vulkan is opt-in, as in <c>ssc</c>.
    /// </summary>
    public static IReadOnlyList<GraphicsBackend> DefaultTargets { get; } =
        [GraphicsBackend.OpenGL, GraphicsBackend.D3D11, GraphicsBackend.D3D12];

    /// <summary>
    /// Backends shaders are cooked for, in the same grammar and with the same
    /// default as <c>ssc</c>.
    /// </summary>
    public IReadOnlyList<GraphicsBackend> Targets { get; init; } = DefaultTargets;

    /// <summary>
    /// Requested worker count. The scheduler clamps it to the amount of work;
    /// see <see cref="CookResult.Workers"/>. Output is byte-identical at any count.
    /// </summary>
    public int Jobs { get; init; } = 1;

    /// <summary>Whether the content-addressed cache may be read and written.</summary>
    public bool UseCache { get; init; } = true;

    /// <summary>
    /// Emit a cooked directory tree instead of a pack: the overlay input for the
    /// editor's cooked-accurate preview.
    /// </summary>
    public bool Loose { get; init; }

    /// <summary>Keep authored brush geometry in a cooked map, so a verify can recompile it.</summary>
    public bool KeepBrushSource { get; init; }

    /// <summary>Whether cooked scripts keep their source text.</summary>
    public ScriptSourceMode ScriptSource { get; init; } = ScriptSourceMode.Embed;

    /// <summary>Which block-compression encoder to use.</summary>
    public CookEncoder Encoder { get; init; } = CookEncoder.Managed;

    /// <summary>
    /// The one rate every cooked sound is resampled to. Changing it re-cooks
    /// every sound in the project (<see cref="CookSettingKeys.AudioSampleRate"/>).
    /// </summary>
    // A project setting, not a per-run one, so it has no command-line switch.
    // It should move to the project manifest.
    public int AudioSampleRate { get; init; } = DefaultAudioSampleRate;

    /// <summary>The project audio rate a cook uses when nobody names one.</summary>
    public const int DefaultAudioSampleRate = 48_000;

    /// <summary>The highest rate a cook will resample to; the reader's own bound.</summary>
    public const int MaxAudioSampleRate = 768_000;

    /// <summary>
    /// Promote every warning to an error.
    /// </summary>
    public bool Strict { get; init; }

    /// <summary>
    /// Where to write the cook manifest, or null for none. This is the artifact
    /// CI diffs: every asset, its id, its inputs and its output hash.
    /// </summary>
    public string? ManifestPath { get; init; }

    /// <summary>Validates what can be validated without touching a project.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="Jobs"/> is below one, or <see cref="AudioSampleRate"/> is
    /// outside what a cooked sound can declare.
    /// </exception>
    public void Validate()
    {
        if (Jobs < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Jobs), Jobs, "A cook runs at least one job.");
        }

        if (AudioSampleRate is < 1 or > MaxAudioSampleRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AudioSampleRate),
                AudioSampleRate,
                $"A project audio rate is between 1 and {MaxAudioSampleRate} frames a second.");
        }
    }
}
