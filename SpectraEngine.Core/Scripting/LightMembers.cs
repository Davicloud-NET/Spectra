using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Core.Scripting;

// What a script reads and sets on a light, for ScriptWrites. A light is
// changed in place and tells nobody: the renderer reads every light again
// on each frame, so the next one shows the change.
internal static class LightMembers
{
    // None for a node that is no light, and for the range of a directional
    // light, which has none. A part that carries a light is a part.
    public static MemberValue Read(SceneNode node, SettableMemberId member)
    {
        if (node.Light is not { } light || NodeClass.KindOf(node) != NodeClass.OfLight(light.Kind))
            return MemberValue.None;

        return member switch
        {
            SettableMemberId.Enabled => MemberValue.Of(light.Enabled),
            SettableMemberId.Brightness => MemberValue.Of(light.Intensity),
            SettableMemberId.Color => MemberValue.Of(light.DisplayColor),
            SettableMemberId.Range when light.Kind != LightKind.Directional => MemberValue.Of(light.Range),
            _ => MemberValue.None,
        };
    }

    // The value is of the member's kind, and the light is one that has the member.
    public static bool TryWrite(
        PlayJournal journal, Light light, SettableMemberId member, in MemberValue value,
        [NotNullWhen(false)] out string? refusal)
    {
        refusal = member switch
        {
            SettableMemberId.Enabled => SetEnabled(journal, light, value.Boolean),
            SettableMemberId.Brightness => SetBrightness(journal, light, (float)value.Number),
            SettableMemberId.Color => SetColor(journal, light, value.Color),
            _ => SetRange(journal, light, (float)value.Number),
        };

        return refusal is null;
    }

    private static string? SetEnabled(PlayJournal journal, Light light, bool value)
    {
        if (value == light.Enabled)
            return null;

        journal.Record(light, LightJournalKind.Enabled);
        light.Enabled = value;
        return null;
    }

    // In each setter the value the light already has comes first: it is
    // left alone and records nothing, whatever a level was saved with.
    private static string? SetBrightness(PlayJournal journal, Light light, float value)
    {
        if (value == light.Intensity)
            return null;

        // False for what is no number, too.
        if (!(value >= 0f) || float.IsInfinity(value))
            return Refused("Brightness", value, "of 0 or more");
        if (!Shines(light.Color, value))
            return TooMuch("Brightness", "Color", string.Create(CultureInfo.InvariantCulture, $"{value}"));

        journal.Record(light, LightJournalKind.Intensity);
        light.Intensity = value;
        return null;
    }

    private static string? SetRange(PlayJournal journal, Light light, float value)
    {
        if (value == light.Range)
            return null;

        if (!(value > 0f) || float.IsInfinity(value))
            return Refused("Range", value, "above 0");

        journal.Record(light, LightJournalKind.Range);
        light.Range = value;
        return null;
    }

    private static string? SetColor(PlayJournal journal, Light light, Color3 value)
    {
        // Compared as the colour a script reads. Its linear value can be a
        // rounding step from the one the light holds, and a write of what
        // was read would then change the light.
        if (value == light.DisplayColor)
            return null;

        if (!IsPart(value.R) || !IsPart(value.G) || !IsPart(value.B))
        {
            return $"Color cannot be {Written(value)}: " +
                "each of red, green and blue is a number of 0 or more that is not infinite.";
        }

        if (!Shines(value.ToLinear(), light.Intensity))
            return TooMuch("Color", "Brightness", Written(value));

        journal.Record(light, LightJournalKind.Color);
        light.DisplayColor = value;
        return null;
    }

    // A part above 1 is a brighter light. One below 0 would take light away.
    private static bool IsPart(float part) => part >= 0f && !float.IsInfinity(part);

    // The renderer is handed the linear colour times the brightness. Each
    // can be a number while that is none: a colour part of 1e17 is past the
    // largest float once it is linear.
    private static bool Shines(Vector3 linear, float intensity)
    {
        Vector3 radiance = linear * intensity;
        return float.IsFinite(radiance.X) && float.IsFinite(radiance.Y) && float.IsFinite(radiance.Z);
    }

    private static string TooMuch(string member, string other, string value) =>
        $"{member} cannot be {value}: with the light's {other} that is more light than a number holds.";

    private static string Written(Color3 value) =>
        string.Create(CultureInfo.InvariantCulture, $"{value.R}, {value.G}, {value.B}");

    private static string Refused(string member, float value, string takes) => string.Create(
        CultureInfo.InvariantCulture, $"{member} cannot be {value}: it takes a number {takes} that is not infinite.");
}
