using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// One value of a member a script can set: what the script hands over, and
/// what the member reads back as. Two are equal when they are of one kind
/// and hold equal values.
/// </summary>
// One kind at a time, so the kinds share their bytes: 40 in all. Short
// text is kept in them too, and needs no array.
[StructLayout(LayoutKind.Explicit)]
public readonly struct MemberValue : IEquatable<MemberValue>
{
    /// <summary>The longest text, in bytes of UTF-8, that is kept in the value itself.</summary>
    public const int InlineUtf8Bytes = 28;

    // A string, the UTF-8 of a longer text, or a node. Text with none is in
    // _inline.
    [FieldOffset(0)]
    private readonly object? _reference;

    [FieldOffset(8)]
    private readonly Vector3 _vector;

    [FieldOffset(8)]
    private readonly double _number;

    [FieldOffset(8)]
    private readonly CFrame _frame;

    [FieldOffset(8)]
    private readonly InlineUtf8 _inline;

    [FieldOffset(36)]
    private readonly MemberValueKind _kind;

    [FieldOffset(37)]
    private readonly byte _inlineLength;

    private MemberValue(MemberValueKind kind, object? reference)
    {
        this = default;
        _kind = kind;
        _reference = reference;
    }

    private MemberValue(MemberValueKind kind, Vector3 vector)
    {
        this = default;
        _kind = kind;
        _vector = vector;
    }

    private MemberValue(MemberValueKind kind, double number)
    {
        this = default;
        _kind = kind;
        _number = number;
    }

    private MemberValue(ReadOnlySpan<byte> shortUtf8)
    {
        this = default;
        _kind = MemberValueKind.Text;
        _inlineLength = (byte)shortUtf8.Length;
        shortUtf8.CopyTo(_inline);
    }

    private MemberValue(CFrame frame)
    {
        this = default;
        _kind = MemberValueKind.CFrame;
        _frame = frame;
    }

    /// <summary>No value. This is the default.</summary>
    public static MemberValue None => default;

    /// <summary>Which of the members below holds the value.</summary>
    public MemberValueKind Kind => _kind;

    /// <summary>
    /// The value of a <see cref="MemberValueKind.Text"/>. Never null. Text
    /// held as UTF-8 is decoded on each read.
    /// </summary>
    public string Text => _reference switch
    {
        string text => text,
        _ when _kind == MemberValueKind.Text => Encoding.UTF8.GetString(Utf8),
        _ => string.Empty,
    };

    /// <summary>
    /// Whether a <see cref="MemberValueKind.Text"/> is held as UTF-8, which
    /// <see cref="Utf8"/> hands out as it is.
    /// </summary>
    public bool IsUtf8 => _kind == MemberValueKind.Text && _reference is not string;

    /// <summary>
    /// The bytes of a <see cref="MemberValueKind.Text"/> as UTF-8. Text made
    /// from a string is encoded on each read.
    /// </summary>
    [UnscopedRef]
    public ReadOnlySpan<byte> Utf8 => _reference switch
    {
        null when _kind == MemberValueKind.Text => ((ReadOnlySpan<byte>)_inline)[.._inlineLength],
        byte[] utf8 when _kind == MemberValueKind.Text => utf8,
        string text => Encoding.UTF8.GetBytes(text),
        _ => default,
    };

    /// <summary>
    /// How many bytes of text the value keeps outside itself: the UTF-8 of text longer
    /// than <see cref="InlineUtf8Bytes"/>, or two for each character of text made from a
    /// string. Nothing for short text and for every other kind.
    /// </summary>
    public int OutsideBytes => _reference switch
    {
        byte[] utf8 => utf8.Length,
        string text => 2 * text.Length,
        _ => 0,
    };

    /// <summary>The value of a <see cref="MemberValueKind.Vector3"/>.</summary>
    public Vector3 Vector => _kind is MemberValueKind.Vector3 or MemberValueKind.Color ? _vector : default;

    /// <summary>The value of a <see cref="MemberValueKind.CFrame"/>.</summary>
    public CFrame Frame => _kind == MemberValueKind.CFrame ? _frame : default;

    /// <summary>The value of a <see cref="MemberValueKind.Boolean"/>.</summary>
    public bool Boolean => Number != 0d;

    /// <summary>The value of a <see cref="MemberValueKind.Number"/>.</summary>
    public double Number => _kind is MemberValueKind.Number or MemberValueKind.Boolean ? _number : 0d;

    /// <summary>The value of a <see cref="MemberValueKind.Color"/>.</summary>
    public Color3 Color
    {
        get
        {
            Vector3 vector = Vector;
            return new Color3(vector.X, vector.Y, vector.Z);
        }
    }

    /// <summary>The value of a <see cref="MemberValueKind.Node"/>. Null is no node, which is a value too.</summary>
    public SceneNode? Node => _reference as SceneNode;

    /// <summary>Text.</summary>
    public static MemberValue Of(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new MemberValue(MemberValueKind.Text, value);
    }

    /// <summary>
    /// Text as bytes of UTF-8, copied. The bytes need not be valid UTF-8,
    /// and are kept as they are.
    /// </summary>
    public static MemberValue OfUtf8(ReadOnlySpan<byte> value) =>
        value.Length <= InlineUtf8Bytes ? new(value) : new(MemberValueKind.Text, value.ToArray());

    /// <summary>Three numbers.</summary>
    public static MemberValue Of(Vector3 value) => new(MemberValueKind.Vector3, value);

    /// <summary>A position and a rotation.</summary>
    public static MemberValue Of(CFrame value) => new(value);

    /// <summary>True or false.</summary>
    public static MemberValue Of(bool value) => new(MemberValueKind.Boolean, value ? 1d : 0d);

    /// <summary>A number.</summary>
    public static MemberValue Of(double value) => new(MemberValueKind.Number, value);

    /// <summary>A colour as a person picks one.</summary>
    public static MemberValue Of(Color3 value) => new(MemberValueKind.Color, new Vector3(value.R, value.G, value.B));

    /// <summary>A node, or null for no node.</summary>
    public static MemberValue OfNode(SceneNode? value) => new(MemberValueKind.Node, (object?)value);

    /// <summary>
    /// The value with its text held as UTF-8. This value when it holds no
    /// text or holds it so already.
    /// </summary>
    public MemberValue AsUtf8()
    {
        if (_reference is not string text)
            return this;

        Span<byte> room = stackalloc byte[InlineUtf8Bytes];
        return Encoding.UTF8.TryGetBytes(text, room, out int written)
            ? new MemberValue(room[..written])
            : new MemberValue(MemberValueKind.Text, Encoding.UTF8.GetBytes(text));
    }

    /// <summary>What a script calls a value of this kind.</summary>
    public static string TypeName(MemberValueKind kind) => kind switch
    {
        MemberValueKind.Text => "string",
        MemberValueKind.Vector3 => "Vector3",
        MemberValueKind.CFrame => "CFrame",
        MemberValueKind.Boolean => "boolean",
        MemberValueKind.Number => "number",
        MemberValueKind.Color => "Color3",
        MemberValueKind.Node => "Instance",
        _ => "nil",
    };

    /// <summary>Whether both are of one kind and hold equal values. Text is compared by what it says.</summary>
    public bool Equals(MemberValue other)
    {
        if (_kind != other._kind)
            return false;

        return _kind switch
        {
            MemberValueKind.Text => TextEquals(in other),
            MemberValueKind.Vector3 or MemberValueKind.Color =>
                EqualityComparer<Vector3>.Default.Equals(_vector, other._vector),
            MemberValueKind.CFrame => EqualityComparer<CFrame>.Default.Equals(_frame, other._frame),
            MemberValueKind.Boolean or MemberValueKind.Number =>
                EqualityComparer<double>.Default.Equals(_number, other._number),
            MemberValueKind.Node => EqualityComparer<SceneNode?>.Default.Equals(Node, other.Node),
            _ => true,
        };
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is MemberValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _kind switch
    {
        MemberValueKind.Text => HashCode.Combine(
            _kind, _reference is string text ? Utf8Text.HashOf(text) : Utf8Text.HashOf(Utf8)),
        MemberValueKind.Vector3 or MemberValueKind.Color => HashCode.Combine(_kind, _vector),
        MemberValueKind.CFrame => HashCode.Combine(_kind, _frame),
        MemberValueKind.Boolean or MemberValueKind.Number => HashCode.Combine(_kind, _number),
        MemberValueKind.Node => HashCode.Combine(_kind, Node),
        _ => 0,
    };

    /// <summary>Whether both are of one kind and hold equal values.</summary>
    public static bool operator ==(MemberValue left, MemberValue right) => left.Equals(right);

    /// <summary>Whether they differ in kind or value.</summary>
    public static bool operator !=(MemberValue left, MemberValue right) => !left.Equals(right);

    private bool TextEquals(in MemberValue other) => (_reference, other._reference) switch
    {
        (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
        (string a, _) => Utf8Text.Says(other.Utf8, a),
        (_, string b) => Utf8Text.Says(Utf8, b),
        _ => Utf8.SequenceEqual(other.Utf8),
    };

    /// <summary>
    /// The value as text, the same on every machine. A number is written so
    /// that it reads back as the same number, and a node as its id.
    /// </summary>
    public override string ToString() => _kind switch
    {
        MemberValueKind.Text => Text,
        MemberValueKind.Vector3 => string.Create(
            CultureInfo.InvariantCulture, $"{_vector.X}, {_vector.Y}, {_vector.Z}"),
        MemberValueKind.CFrame => _frame.ToString(),
        MemberValueKind.Boolean => Boolean ? "true" : "false",
        MemberValueKind.Number => _number.ToString(CultureInfo.InvariantCulture),
        MemberValueKind.Color => string.Create(
            CultureInfo.InvariantCulture, $"{_vector.X}, {_vector.Y}, {_vector.Z}"),
        MemberValueKind.Node => Node?.Id.ToString() ?? "nil",
        _ => "nil",
    };

    [InlineArray(InlineUtf8Bytes)]
    private struct InlineUtf8
    {
        private byte _first;
    }
}
