using System;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// What an input takes or an output sends: no value, one value of a
/// <see cref="KeyvalueType"/>, or not said. A schema read from a file that
/// was written before this was recorded does not say, and that is not the
/// same as no value.
/// </summary>
public readonly record struct ParameterType
{
    private const byte NoValue = 1;
    private const byte OneValue = 2;
    private const byte OneValueOrNone = 3;

    private readonly byte _form;
    private readonly KeyvalueType _valueType;

    private ParameterType(byte form, KeyvalueType valueType)
    {
        _form = form;
        _valueType = valueType;
    }

    /// <summary>The schema does not say. This is the default.</summary>
    public static ParameterType NotSaid => default;

    /// <summary>No value.</summary>
    public static ParameterType None => new(NoValue, default);

    /// <summary>One value of <paramref name="type"/>.</summary>
    /// <exception cref="ArgumentException">No wire carries a value of that type.</exception>
    public static ParameterType Of(KeyvalueType type) => new(OneValue, Carried(type));

    /// <summary>
    /// One value of <paramref name="type"/>, or none: an input that also
    /// works with no parameter.
    /// </summary>
    /// <exception cref="ArgumentException">No wire carries a value of that type.</exception>
    public static ParameterType Optional(KeyvalueType type) => new(OneValueOrNone, Carried(type));

    /// <summary>Whether the schema says anything.</summary>
    public bool IsSaid => _form != 0;

    /// <summary>Whether the schema says there is no value.</summary>
    public bool IsNone => _form == NoValue;

    /// <summary>Whether there is a value, and <see cref="ValueType"/> is its type.</summary>
    public bool HasValue => _form is OneValue or OneValueOrNone;

    /// <summary>Whether there is a value that may be left out.</summary>
    public bool IsOptional => _form == OneValueOrNone;

    /// <summary>The type of the value. Means nothing unless <see cref="HasValue"/>.</summary>
    public KeyvalueType ValueType => _valueType;

    /// <summary>
    /// Whether a wire can carry a value of <paramref name="type"/>. A list of
    /// choices, a bit set and a distance only mean something on a keyvalue,
    /// and a level's assets are found through its keyvalues.
    /// </summary>
    public static bool CanCarry(KeyvalueType type) => type switch
    {
        KeyvalueType.Bool or KeyvalueType.Int or KeyvalueType.Float or KeyvalueType.String => true,
        KeyvalueType.Vec2 or KeyvalueType.Vec3 or KeyvalueType.Vec4 => true,
        KeyvalueType.Color or KeyvalueType.Angles => true,
        KeyvalueType.TargetName or KeyvalueType.NodeRef => true,
        _ => false,
    };

    /// <summary>
    /// The type in a word, as a listing prints it: <c>float</c>,
    /// <c>float?</c> for a value that may be left out, <c>none</c>, or
    /// <c>not said</c>.
    /// </summary>
    public override string ToString() => _form switch
    {
        NoValue => "none",
        OneValue => Word(_valueType),
        OneValueOrNone => Word(_valueType) + "?",
        _ => "not said",
    };

    // Written out: the words are printed and documented, so renaming an enum
    // member must not change them.
    private static string Word(KeyvalueType type) => type switch
    {
        KeyvalueType.Bool => "bool",
        KeyvalueType.Int => "int",
        KeyvalueType.Float => "float",
        KeyvalueType.String => "string",
        KeyvalueType.Vec2 => "vec2",
        KeyvalueType.Vec3 => "vec3",
        KeyvalueType.Vec4 => "vec4",
        KeyvalueType.Color => "color",
        KeyvalueType.Angles => "angles",
        KeyvalueType.TargetName => "targetname",
        KeyvalueType.NodeRef => "noderef",
        _ => "",
    };

    private static KeyvalueType Carried(KeyvalueType type) => CanCarry(type)
        ? type
        : throw new ArgumentException(
            $"No wire carries a {type}. An input or output carries a Bool, Int, Float, String, Vec2, Vec3, " +
            "Vec4, Color, Angles, TargetName or NodeRef.",
            nameof(type));
}
