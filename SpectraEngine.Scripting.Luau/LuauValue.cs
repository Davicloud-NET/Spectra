using System.Numerics;

namespace SpectraEngine.Scripting.Luau;

/// <summary>
/// One value on its way into Luau: an argument for a function or what a
/// thread is resumed with. A span of these lives on the stack, so passing
/// numbers, booleans and vectors allocates nothing.
/// </summary>
public readonly struct LuauValue
{
    private readonly object? _object;
    private readonly double _number;
    private readonly Vector3 _vector;
    private readonly int _tag;
    private readonly Kind _kind;

    private LuauValue(Kind kind, object? value = null, double number = 0, Vector3 vector = default, int tag = 0)
    {
        _kind = kind;
        _object = value;
        _number = number;
        _vector = vector;
        _tag = tag;
    }

    private enum Kind : byte
    {
        Nil,
        Boolean,
        Number,
        Vector,
        Text,
        Reference,
        Function,
        Object,
    }

    /// <summary>Nil, which is also what <c>default</c> is.</summary>
    public static LuauValue Nil => default;

    /// <summary>
    /// A managed object, as the userdata that stands for it. The tag is one that
    /// <see cref="LuauObjects.SetMetatable"/> has set up.
    /// </summary>
    public static LuauValue Object(int tag, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new LuauValue(Kind.Object, value, tag: tag);
    }

    public static implicit operator LuauValue(bool value) => new(Kind.Boolean, number: value ? 1 : 0);

    public static implicit operator LuauValue(double value) => new(Kind.Number, number: value);

    public static implicit operator LuauValue(Vector3 value) => new(Kind.Vector, vector: value);

    public static implicit operator LuauValue(string? value) => value is null ? default : new(Kind.Text, value);

    public static implicit operator LuauValue(LuauReference? value) => value is null ? default : new(Kind.Reference, value);

    public static implicit operator LuauValue(LuauFunction? value) => value is null ? default : new(Kind.Function, value);

    // False: the push could not be made and an error value is on top instead.
    internal bool PushTo(LuauHost host, LuauStack stack) => _kind switch
    {
        Kind.Boolean => stack.Push(_number != 0),
        Kind.Number => stack.Push(_number),
        Kind.Vector => stack.Push(_vector),
        Kind.Text when _object is string text => stack.Push(text),
        Kind.Reference when _object is LuauReference reference => reference.Push(stack),
        Kind.Function when _object is LuauFunction function => function.Push(stack),
        Kind.Object when _object is { } value => host.Objects.Push(stack, _tag, value),
        _ => stack.PushNil(),
    };
}
