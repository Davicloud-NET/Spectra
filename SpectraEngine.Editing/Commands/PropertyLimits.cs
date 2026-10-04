using SpectraEngine.Core.Inspection;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// What a property accepts, shared by the editor's refusal and the panel's
/// message.
/// </summary>
// The Light setters throw on these values. Strings complete
// "Not applied: Range must be ...".
public static class PropertyLimits
{
    /// <summary>Why <paramref name="value"/> would be refused, or null when it is fine.</summary>
    public static string? Refusal(PropertyId id, float value) => id switch
    {
        PropertyId.LightRange when !float.IsFinite(value) || value <= 0f => "greater than 0",

        PropertyId.LightIntensity when !float.IsFinite(value) || value < 0f => "0 or more",

        _ => null,
    };

    /// <summary>What this cell accepts, for the message shown when input was rejected.</summary>
    public static string Expected(PropertyId id, PropertyKind kind)
    {
        if (kind == PropertyKind.Color) return "#RRGGBB";

        return id switch
        {
            PropertyId.LightRange => "a number greater than 0",
            PropertyId.LightIntensity => "a number of 0 or more",
            _ => "a number",
        };
    }
}
