using System.Globalization;
using System.Numerics;
using System.Text;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// Writes to the members a script can set, drawn from a seed: every member,
// with values a node takes and values it refuses. The same seed gives the
// same writes, so a failure can be run again.
internal sealed class RandomWrites(int seed)
{
    // Zero with both signs: a transform that differs only there is equal as
    // numbers and not as bytes.
    private static readonly float[] Numbers = [0f, -0f, 1f, -1f, 0.5f, -2.25f, 3.75f, 100f, -1e6f, 1e-3f];

    private static readonly float[] NotNumbers = [float.NaN, float.PositiveInfinity, float.NegativeInfinity];

    // What a brightness and a range both take, and what both refuse.
    private static readonly float[] Amounts = [0.5f, 1f, 2.25f, 3.75f, 100f, 1e-3f];

    private static readonly float[] RefusedAmounts =
        [-1f, -2.25f, -1e6f, float.NaN, float.PositiveInfinity, float.NegativeInfinity];

    // A part of a colour: both ends, a step of a picker, and past white.
    private static readonly float[] ColorParts = [0f, 1f, 0.5f, 128f / 255f, 0.2f, 2f];

    private static readonly string[] Names =
        ["Door", "Gate 2", "Tür", "a", "wild*", "\U0001F6AA", new string('x', NodeName.MaxLength)];

    private static readonly string[] RefusedNames =
    [
        "", "   ", "two\nlines", "tab\there", "two\u2028lines", " Door", "Door ", "!self",
        new string('x', NodeName.MaxLength + 1),
    ];

    // Luau values of no kind a member takes. The door is handed no value.
    private static readonly string[] OtherLuau = ["nil", "5", "true", "{}", "workspace", "print"];

    private static readonly MemberValueKind[] Kinds =
    [
        MemberValueKind.Text, MemberValueKind.Vector3, MemberValueKind.CFrame, MemberValueKind.Boolean,
        MemberValueKind.Number, MemberValueKind.Color,
    ];

    private readonly Random _random = new(seed);

    public int Pick(int count) => _random.Next(count);

    // Seven writes in eight are to a member the target has, when one is
    // named. A light's member written to a part is refused every time, and
    // a run of mostly such writes would show little.
    public RandomWrite Next(SceneNode? target = null)
    {
        IReadOnlyList<SettableMember> members = ScriptWrites.Settable;
        SettableMember member = members[Pick(members.Count)];
        if (target is not null && Pick(8) != 0)
        {
            while (!ScriptWrites.Has(target, member.Id))
                member = members[Pick(members.Count)];
        }

        // A script that moves a thing and then puts it where it was is common,
        // and it is how a node ends a run equal to its first value.
        if (Pick(8) == 0)
            return new RandomWrite(member, MemberValue.None, string.Empty, PutsBack: true);

        // One write in six hands over the wrong kind of value, and one in
        // five of the rest a value of the right kind that is refused.
        if (Pick(6) == 0)
            return WrongKind(member);

        (MemberValue value, string luau) = Value(member.Kind, isRefused: Pick(5) == 0);
        return new RandomWrite(member, value, luau);
    }

    // A write of a value the member takes. A run makes one to every member
    // of every node first, so no seed leaves one out.
    public RandomWrite Taken(SettableMember member)
    {
        (MemberValue value, string luau) = Value(member.Kind, isRefused: false);
        return new RandomWrite(member, value, luau);
    }

    private RandomWrite WrongKind(SettableMember member)
    {
        if (Pick(2) == 0)
        {
            // Not the one of them that is of the member's own kind.
            string other = OtherLuau[Pick(OtherLuau.Length)];
            while (IsOfKind(other, member.Kind))
                other = OtherLuau[Pick(OtherLuau.Length)];

            return new RandomWrite(member, MemberValue.None, other);
        }

        MemberValueKind kind = Kinds[Pick(Kinds.Length)];
        while (kind == member.Kind)
            kind = Kinds[Pick(Kinds.Length)];

        (MemberValue value, string luau) = Value(kind, isRefused: false);
        return new RandomWrite(member, value, luau);
    }

    private (MemberValue Value, string Luau) Value(MemberValueKind kind, bool isRefused)
    {
        switch (kind)
        {
            case MemberValueKind.Vector3:
                Vector3 vector = Vector(isRefused);
                return (MemberValue.Of(vector), $"Vector3.new({Luau(vector)})");

            case MemberValueKind.CFrame:
                Vector3 at = Vector(isRefused);
                Vector3 turn = new(Number(), Number(), Number());
                return (
                    MemberValue.Of(new CFrame(at) * CFrame.Angles(turn.X, turn.Y, turn.Z)),
                    $"CFrame.new({Luau(at)}) * CFrame.Angles({Luau(turn)})");

            case MemberValueKind.Text:
                return Name(isRefused);

            // Every boolean is taken. What refuses one is the node.
            case MemberValueKind.Boolean:
                bool flag = Pick(2) == 0;
                return (MemberValue.Of(flag), flag ? "true" : "false");

            case MemberValueKind.Number:
                float amount = Amount(isRefused);
                return (MemberValue.Of(amount), Luau(amount));

            case MemberValueKind.Color:
                Vector3 parts = ColorOf(isRefused);
                return (MemberValue.Of(new Color3(parts.X, parts.Y, parts.Z)), $"Color3.new({Luau(parts)})");

            default:
                throw new NotSupportedException(
                    $"RandomWrites makes no value of the kind {kind}. Add one, so the run covers the new member.");
        }
    }

    private (MemberValue Value, string Luau) Name(bool isRefused)
    {
        // Bytes that are no text exist only in Luau, so the door gets the
        // empty name in their place. Both are refused.
        if (isRefused && Pick(6) == 0)
            return (MemberValue.Of(""), "\"\\xff\\xfe\"");

        string[] pool = isRefused ? RefusedNames : Names;
        string name = !isRefused && Pick(3) == 0 ? $"Node {Pick(1000)}" : pool[Pick(pool.Length)];
        return (MemberValue.Of(name), Luau(name));
    }

    private Vector3 Vector(bool isRefused)
    {
        float[] parts = [Number(), Number(), Number()];
        if (isRefused)
            parts[Pick(3)] = NotNumbers[Pick(NotNumbers.Length)];

        return new Vector3(parts);
    }

    private static bool IsOfKind(string luau, MemberValueKind kind) =>
        (luau == "5" && kind == MemberValueKind.Number) || (luau == "true" && kind == MemberValueKind.Boolean);

    private float Amount(bool isRefused)
    {
        if (isRefused)
            return RefusedAmounts[Pick(RefusedAmounts.Length)];

        return Pick(3) == 0 ? (float)((_random.NextDouble() * 50d) + 0.01d) : Amounts[Pick(Amounts.Length)];
    }

    // One part is always drawn afresh, so no two colours of a run are alike
    // and none is the white a light starts with.
    private Vector3 ColorOf(bool isRefused)
    {
        float[] parts =
        [
            ColorParts[Pick(ColorParts.Length)], ColorParts[Pick(ColorParts.Length)], ColorParts[Pick(ColorParts.Length)],
        ];
        parts[Pick(3)] = (float)_random.NextDouble();
        if (isRefused)
            parts[Pick(3)] = RefusedAmounts[Pick(RefusedAmounts.Length)];

        return new Vector3(parts);
    }

    private float Number() =>
        Pick(3) == 0 ? (float)((_random.NextDouble() * 100d) - 50d) : Numbers[Pick(Numbers.Length)];

    private static string Luau(Vector3 value) => $"{Luau(value.X)}, {Luau(value.Y)}, {Luau(value.Z)}";

    // As a double, written so that Luau reads the same float back.
    private static string Luau(float value)
    {
        if (float.IsNaN(value))
            return "0/0";
        if (float.IsInfinity(value))
            return value > 0f ? "math.huge" : "-math.huge";

        return ((double)value).ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Luau(string text)
    {
        var literal = new StringBuilder("\"");
        foreach (char character in text)
        {
            literal.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                < ' ' => string.Create(CultureInfo.InvariantCulture, $"\\{(int)character:D3}"),
                _ => character.ToString(),
            });
        }

        return literal.Append('"').ToString();
    }
}
