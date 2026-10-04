using Spectra.Kitchen.Rules;
using SpectraEngine.Core;
using SpectraEngine.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Spectra.Kitchen.Cooking;

/// <summary>
/// The cook manifest: every asset, its id, its inputs and its output hash, as
/// canonical JSON. This is the file CI diffs.
/// </summary>
// No timestamps, absolute paths or machine names: two cooks of one tree must
// write the same bytes.
public static class CookManifest
{
    /// <summary>Format version of the manifest document itself.</summary>
    public const int FormatVersion = 1;

    /// <summary>Renders the manifest for one cook.</summary>
    public static byte[] Write(string projectName, CookProfile profile, IReadOnlyList<CookedAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var records = new List<byte[]>(assets.Count);
        foreach (CookedAsset asset in assets)
            records.Add(CanonicalJson.Compact(w => WriteAsset(w, asset)));

        return CanonicalJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("scookManifest", FormatVersion);
            writer.WriteString("engine", EngineInfo.VersionString);
            writer.WriteString("project", projectName);
            writer.WriteString("profile", ToWire(profile));
            CanonicalJson.WriteRecordArray(writer, "assets", records);
            writer.WriteEndObject();
        });
    }

    /// <summary>The profile's spelling, which is the command line's.</summary>
    public static string ToWire(CookProfile profile) => profile switch
    {
        CookProfile.Ship => "ship",
        CookProfile.Fast => "fast",
        CookProfile.Preview => "preview",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown cook profile."),
    };

    /// <summary>The rule kind's spelling.</summary>
    public static string ToWire(RuleKind kind) => kind switch
    {
        RuleKind.RawCopy => "rawcopy",
        RuleKind.Image => "image",
        RuleKind.Model => "model",
        RuleKind.Audio => "audio",
        RuleKind.Material => "material",
        RuleKind.Shader => "shader",
        RuleKind.Script => "script",
        RuleKind.Map => "map",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown rule kind."),
    };

    private static void WriteAsset(Utf8JsonWriter writer, CookedAsset asset)
    {
        writer.WriteStartObject();
        writer.WriteString("path", asset.SourcePath);
        writer.WriteString("rule", ToWire(asset.Rule));
        writer.WriteNumber("ruleVersion", asset.RuleVersion);

        if (asset.FromCache) writer.WriteBoolean("skipped", true);

        writer.WritePropertyName("inputs");
        writer.WriteStartArray();
        foreach (RuleDependency dependency in asset.Dependencies)
        {
            if (dependency.Kind == RuleDependencyKind.ProbeMissing) continue;

            writer.WriteStartObject();
            writer.WriteString("path", dependency.Path);
            writer.WriteString("kind", dependency.Kind == RuleDependencyKind.Read ? "read" : "probe");
            if (dependency.Kind == RuleDependencyKind.Read)
                writer.WriteString("hash", dependency.ContentHash.ToString("X32"));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("missing");
        writer.WriteStartArray();
        foreach (RuleDependency dependency in asset.Dependencies)
        {
            if (dependency.Kind != RuleDependencyKind.ProbeMissing) continue;
            writer.WriteStringValue(dependency.Path);
        }
        writer.WriteEndArray();

        writer.WritePropertyName("outputs");
        writer.WriteStartArray();
        foreach (CookedOutput output in asset.Outputs)
        {
            writer.WriteStartObject();
            writer.WriteString("path", output.Path);
            writer.WriteString("id", output.AssetId.ToString("X32"));
            writer.WriteString("hash", output.ContentHash.ToString("X32"));
            writer.WriteNumber("bytes", output.Length);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }
}
