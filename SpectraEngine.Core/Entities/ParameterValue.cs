using System;
using System.Numerics;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// One value an input takes or an output sends, as code holds it before it
/// is the text a wire carries. <see cref="ParameterText"/> converts between
/// the two.
/// </summary>
public readonly record struct ParameterValue
{
    private readonly Vector4 _vector;
    private readonly double _number;
    private readonly string? _text;
    private readonly Guid _nodeId;
    private readonly byte _vectorSize;

    private ParameterValue(
        ParameterValueKind kind,
        double number = 0d,
        string? text = null,
        Vector4 vector = default,
        byte vectorSize = 0,
        Guid nodeId = default)
    {
        Kind = kind;
        _number = number;
        _text = text;
        _vector = vector;
        _vectorSize = vectorSize;
        _nodeId = nodeId;
    }

    /// <summary>No value. This is the default.</summary>
    public static ParameterValue None => default;

    /// <summary>Which of the members below holds the value.</summary>
    public ParameterValueKind Kind { get; }

    /// <summary>The value of a <see cref="ParameterValueKind.Boolean"/>.</summary>
    public bool Boolean => Kind == ParameterValueKind.Boolean && _number != 0d;

    /// <summary>The value of a <see cref="ParameterValueKind.Number"/>.</summary>
    public double Number => Kind == ParameterValueKind.Number ? _number : 0d;

    /// <summary>The value of a <see cref="ParameterValueKind.Text"/>. Never null.</summary>
    public string Text => _text ?? "";

    /// <summary>
    /// The value of a <see cref="ParameterValueKind.Vector"/>. Only the first
    /// <see cref="VectorSize"/> components count, and the rest are zero.
    /// </summary>
    public Vector4 Vector => _vector;

    /// <summary>How many components a <see cref="ParameterValueKind.Vector"/> has: 2, 3 or 4.</summary>
    public int VectorSize => _vectorSize;

    /// <summary>The value of a <see cref="ParameterValueKind.NodeId"/>: a <c>SceneNode.Id</c>.</summary>
    public Guid NodeId => _nodeId;

    /// <summary>A true or a false.</summary>
    public static ParameterValue Of(bool value) => new(ParameterValueKind.Boolean, number: value ? 1d : 0d);

    /// <summary>A number.</summary>
    public static ParameterValue Of(double value) => new(ParameterValueKind.Number, number: value);

    /// <summary>Text, or the name of another entity.</summary>
    public static ParameterValue Of(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ParameterValue(ParameterValueKind.Text, text: value);
    }

    /// <summary>Two numbers.</summary>
    public static ParameterValue Of(Vector2 value) =>
        new(ParameterValueKind.Vector, vector: new Vector4(value, 0f, 0f), vectorSize: 2);

    /// <summary>Three numbers: a position, a colour, angles in degrees.</summary>
    public static ParameterValue Of(Vector3 value) =>
        new(ParameterValueKind.Vector, vector: new Vector4(value, 0f), vectorSize: 3);

    /// <summary>Four numbers.</summary>
    public static ParameterValue Of(Vector4 value) =>
        new(ParameterValueKind.Vector, vector: value, vectorSize: 4);

    /// <summary>One node, by its <c>SceneNode.Id</c>.</summary>
    public static ParameterValue Of(Guid nodeId) => new(ParameterValueKind.NodeId, nodeId: nodeId);
}
