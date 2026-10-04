using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Spectra.Kitchen.Cache;

/// <summary>
/// Renders the settings a rule declared into the sorted key/value pairs the cache
/// key hashes. Values are spelled as on the command line.
/// </summary>
public static class CookSettingsDigest
{
    /// <summary>
    /// The pairs <paramref name="declared"/> selects out of
    /// <paramref name="settings"/>, sorted ordinal by key.
    /// </summary>
    public static List<KeyValuePair<string, string>> Describe(CookSettings settings, CookSettingKeys declared)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var pairs = new List<KeyValuePair<string, string>>(4);

        if ((declared & CookSettingKeys.Profile) != 0)
            pairs.Add(new("profile", CookManifest.ToWire(settings.Profile)));

        if ((declared & CookSettingKeys.Targets) != 0)
            pairs.Add(new("targets", DescribeTargets(settings.Targets)));

        if ((declared & CookSettingKeys.ScriptSource) != 0)
            pairs.Add(new("scriptSource", ToWire(settings.ScriptSource)));

        if ((declared & CookSettingKeys.Encoder) != 0)
            pairs.Add(new("encoder", ToWire(settings.Encoder)));

        if ((declared & CookSettingKeys.KeepBrushSource) != 0)
            pairs.Add(new("keepBrushSource", settings.KeepBrushSource ? "true" : "false"));

        if ((declared & CookSettingKeys.AudioSampleRate) != 0)
        {
            pairs.Add(new(
                "audioSampleRate",
                settings.AudioSampleRate.ToString(CultureInfo.InvariantCulture)));
        }

        pairs.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return pairs;
    }

    /// <summary>The script source mode's spelling, which is the command line's.</summary>
    public static string ToWire(ScriptSourceMode mode) => mode switch
    {
        ScriptSourceMode.Embed => "embed",
        ScriptSourceMode.Strip => "strip",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown script source mode."),
    };

    /// <summary>The encoder's spelling, which is the command line's.</summary>
    public static string ToWire(CookEncoder encoder) => encoder switch
    {
        CookEncoder.Managed => "managed",
        CookEncoder.Native => "native",
        _ => throw new ArgumentOutOfRangeException(nameof(encoder), encoder, "Unknown cook encoder."),
    };

    /// <summary>The backend's spelling, which is <c>ssc</c>'s and the project manifest's.</summary>
    public static string ToWire(GraphicsBackend backend) => backend switch
    {
        GraphicsBackend.OpenGL => "opengl",
        GraphicsBackend.Vulkan => "vulkan",
        GraphicsBackend.D3D11 => "d3d11",
        GraphicsBackend.D3D12 => "d3d12",
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown graphics backend."),
    };

    // Not sorted: a shader rule emits one blob per target in the order given,
    // so a different order is a different output.
    private static string DescribeTargets(IReadOnlyList<GraphicsBackend> targets)
    {
        if (targets.Count == 0) return string.Empty;

        var text = new StringBuilder(32);
        for (int i = 0; i < targets.Count; i++)
        {
            if (i > 0) text.Append(',');
            text.Append(ToWire(targets[i]));
        }

        return text.ToString();
    }
}
