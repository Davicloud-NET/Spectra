using SpectraEngine.Core.Inspection;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// What a property will accept, in words, so the editor and the panel cannot
/// disagree about it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The refusal and the sentence explaining it have to come from one place.</b>
/// <see cref="PropertyEditor"/> refuses a light range of zero because
/// <c>Light.Range</c> throws rather than clamps, and a command carrying one
/// would throw from inside <c>Do</c> halfway through an open transaction. The
/// panel needs to say why BEFORE posting, and a second hand-written rule there
/// would agree with this one exactly until somebody corrected one of them.
/// </para>
/// <para>
/// <b>The wording is the message, not a code.</b> Every string here is written
/// to complete "Not applied: Range must be ...", because the field it appears
/// under already says which property it is.
/// </para>
/// </remarks>
public static class PropertyLimits
{
    /// <summary>
    /// Why <paramref name="value"/> would be refused, or null when it is fine.
    /// </summary>
    public static string? Refusal(PropertyId id, float value) => id switch
    {
        // Strictly positive: a light with no range lights nothing, and the
        // setter says so by throwing.
        PropertyId.LightRange when !float.IsFinite(value) || value <= 0f => "greater than 0",

        // Zero is legal here and means an unlit light, which is a thing people
        // do while tuning; negative is not a dimmer, it is a throw.
        PropertyId.LightIntensity when !float.IsFinite(value) || value < 0f => "0 or more",

        _ => null,
    };

    /// <summary>
    /// What this cell accepts, for the message shown when it did not.
    /// </summary>
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
