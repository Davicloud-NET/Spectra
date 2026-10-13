using SpectraEngine.Core.Scene;
using System;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// One value of a member a script can set: what the script hands over, and
/// what the member reads back as. Two are equal when they are of one kind
/// and hold equal values.
/// </summary>
public readonly record struct MemberValue
{
    private readonly string? _text;
    private readonly Vector3 _vector;
    private readonly CFrame _frame;
    private readonly double _number;

    private MemberValue(
        MemberValueKind kind, string? text = null, Vector3 vector = default, CFrame frame = default, double number = 0d)
    {
        Kind = kind;
        _text = text;
        _vector = vector;
        _frame = frame;
        _number = number;
    }

    /// <summary>No value. This is the default.</summary>
    public static MemberValue None => default;

    /// <summary>Which of the members below holds the value.</summary>
    public MemberValueKind Kind { get; }

    /// <summary>The value of a <see cref="MemberValueKind.Text"/>. Never null.</summary>
    public string Text => _text ?? "";

    /// <summary>The value of a <see cref="MemberValueKind.Vector3"/>.</summary>
    public Vector3 Vector => _vector;

    /// <summary>The value of a <see cref="MemberValueKind.CFrame"/>.</summary>
    public CFrame Frame => _frame;

    /// <summary>The value of a <see cref="MemberValueKind.Boolean"/>.</summary>
    public bool Boolean => _number != 0d;

    /// <summary>The value of a <see cref="MemberValueKind.Number"/>.</summary>
    public double Number => _number;

    /// <summary>The value of a <see cref="MemberValueKind.Color"/>.</summary>
    public Color3 Color => new(_vector.X, _vector.Y, _vector.Z);

    /// <summary>Text.</summary>
    public static MemberValue Of(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new MemberValue(MemberValueKind.Text, text: value);
    }

    /// <summary>Three numbers.</summary>
    public static MemberValue Of(Vector3 value) => new(MemberValueKind.Vector3, vector: value);

    /// <summary>A position and a rotation.</summary>
    public static MemberValue Of(CFrame value) => new(MemberValueKind.CFrame, frame: value);

    /// <summary>True or false.</summary>
    public static MemberValue Of(bool value) => new(MemberValueKind.Boolean, number: value ? 1d : 0d);

    /// <summary>A number.</summary>
    public static MemberValue Of(double value) => new(MemberValueKind.Number, number: value);

    /// <summary>A colour as a person picks one.</summary>
    public static MemberValue Of(Color3 value) =>
        new(MemberValueKind.Color, vector: new Vector3(value.R, value.G, value.B));

    /// <summary>What a script calls a value of this kind.</summary>
    public static string TypeName(MemberValueKind kind) => kind switch
    {
        MemberValueKind.Text => "string",
        MemberValueKind.Vector3 => "Vector3",
        MemberValueKind.CFrame => "CFrame",
        MemberValueKind.Boolean => "boolean",
        MemberValueKind.Number => "number",
        MemberValueKind.Color => "Color3",
        _ => "nil",
    };

    /// <summary>
    /// The value as text, the same on every machine. A number is written so
    /// that it reads back as the same number.
    /// </summary>
    public override string ToString() => Kind switch
    {
        MemberValueKind.Text => Text,
        MemberValueKind.Vector3 => string.Create(
            CultureInfo.InvariantCulture, $"{_vector.X}, {_vector.Y}, {_vector.Z}"),
        MemberValueKind.CFrame => _frame.ToString(),
        MemberValueKind.Boolean => Boolean ? "true" : "false",
        MemberValueKind.Number => _number.ToString(CultureInfo.InvariantCulture),
        MemberValueKind.Color => string.Create(
            CultureInfo.InvariantCulture, $"{_vector.X}, {_vector.Y}, {_vector.Z}"),
        _ => "nil",
    };
}
