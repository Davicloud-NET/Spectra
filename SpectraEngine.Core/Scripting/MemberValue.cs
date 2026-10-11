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

    private MemberValue(MemberValueKind kind, string? text = null, Vector3 vector = default, CFrame frame = default)
    {
        Kind = kind;
        _text = text;
        _vector = vector;
        _frame = frame;
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

    /// <summary>What a script calls a value of this kind.</summary>
    public static string TypeName(MemberValueKind kind) => kind switch
    {
        MemberValueKind.Text => "string",
        MemberValueKind.Vector3 => "Vector3",
        MemberValueKind.CFrame => "CFrame",
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
        _ => "nil",
    };
}
