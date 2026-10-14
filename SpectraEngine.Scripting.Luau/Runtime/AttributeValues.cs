using System.Numerics;
using System.Text.Unicode;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The value of an attribute between a script and what is kept of it: a
// string, a boolean, a number, a Vector3, a CFrame or a Color3. Nil is no
// value, which takes an attribute away.
internal static class AttributeValues
{
    private const string Kinds =
        "An attribute holds a string, a boolean, a number, a Vector3, a CFrame or a Color3, and nil takes it away";

    // Reads the value a script handed over. `held` is what the attribute
    // holds now: text that equals it is handed back as it is, so a script
    // that sets the same word every tick allocates nothing. False for a value
    // no attribute holds, and `refusal` then says so.
    public static bool TryRead(LuauStack stack, int index, in MemberValue held, out MemberValue value, out string? refusal)
    {
        refusal = null;
        value = MemberValue.None;
        switch (stack.TypeOf(index))
        {
            case LuauType.None or LuauType.Nil:
                return true;
            case LuauType.Boolean:
                value = MemberValue.Of(stack.GetBoolean(index));
                return true;
            case LuauType.Number or LuauType.Integer when stack.TryGetNumber(index, out double number):
                value = MemberValue.Of(number);
                return true;
            case LuauType.Vector when stack.TryGetVector(index, out Vector3 vector):
                value = MemberValue.Of(vector);
                return true;
            case LuauType.String when stack.TryGetUtf8(index, out ReadOnlySpan<byte> text):
                return TryReadText(text, in held, out value, out refusal);
        }

        if (FrameValue.TryGet(stack, index, out CFrame frame))
            value = MemberValue.Of(frame);
        else if (ColorValue.TryGet(stack, index, out Color3 color))
            value = MemberValue.Of(color);
        else
            refusal = $"{ScriptArguments.TypeName(stack, index)} is not a supported attribute type. {Kinds}";

        return refusal is null;
    }

    // Nil for no value. False: it could not be pushed, and an error value is
    // on top instead.
    public static bool Push(LuauStack stack, in MemberValue value) => value.Kind switch
    {
        MemberValueKind.Text => PushText(stack, in value),
        MemberValueKind.Boolean => stack.Push(value.Boolean),
        MemberValueKind.Number => stack.Push(value.Number),
        MemberValueKind.Vector3 => stack.Push(value.Vector),
        MemberValueKind.CFrame => FrameValue.Push(stack, value.Frame),
        MemberValueKind.Color => ColorValue.Push(stack, value.Color),
        _ => stack.PushNil(),
    };

    // An entity's keyvalue is a string. What a script set is its own bytes.
    private static bool PushText(LuauStack stack, in MemberValue value) =>
        value.IsUtf8 ? stack.PushUtf8(value.Utf8) : stack.Push(value.Text);

    // A string of Luau is bytes, and they are kept as they are. Bytes that
    // are no text are refused, since a level can hold no such attribute. The
    // text is kept outside the memory Luau counts for the scripts, so its
    // length has a bound here.
    private static bool TryReadText(ReadOnlySpan<byte> text, in MemberValue held, out MemberValue value, out string? refusal)
    {
        value = MemberValue.None;
        refusal = null;
        if (text.Length > AttributeLimits.MaxTextBytes)
        {
            refusal = $"A string of {text.Length} bytes is not a supported attribute value. " +
                $"An attribute's string is at most {AttributeLimits.MaxTextBytes} bytes long";
        }
        else if (!Utf8.IsValid(text))
        {
            refusal = $"A string that is not valid UTF-8 is not a supported attribute value. {Kinds}";
        }
        else
        {
            value = held.IsUtf8 && text.SequenceEqual(held.Utf8) ? held : MemberValue.OfUtf8(text);
        }

        return refusal is null;
    }
}
