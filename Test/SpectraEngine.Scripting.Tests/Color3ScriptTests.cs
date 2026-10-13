using System.Globalization;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Color3 in a script against <see cref="Color3"/> in the engine. The
/// userdata holds the engine's value, so the two must agree to the bit.
/// Where Roblox's documentation says nothing, the test says what was chosen.
/// </summary>
public sealed class Color3ScriptTests
{
    private const string Shared = """
        local c = Color3.new(0.25, 0.5, 1)
        local d = Color3.fromRGB(255, 128, 0)
        """;

    private static Color3 C => new(0.25f, 0.5f, 1f);

    private static Color3 D => Color3.FromRgb(255f, 128f, 0f);

    [Fact]
    public void Every_way_of_making_a_colour_makes_the_one_the_engine_makes()
    {
        (string Luau, Color3 Core)[] cases =
        [
            ("Color3.new(0.1, 0.2, 0.3)", new Color3(0.1f, 0.2f, 0.3f)),
            ("Color3.new(1, 0, 0)", new Color3(1f, 0f, 0f)),
            ("Color3.fromRGB(255, 128, 0)", Color3.FromRgb(255f, 128f, 0f)),
            ("Color3.fromRGB(12.5, 0, 300)", Color3.FromRgb(12.5f, 0f, 300f)),
            ("Color3.fromHSV(0.6, 0.5, 0.75)", Color3.FromHsv(0.6f, 0.5f, 0.75f)),
            ("Color3.fromHSV(1, 1, 1)", new Color3(1f, 0f, 0f)),
            ("Color3.fromHex('#ff8000')", Color3.FromRgb(255f, 128f, 0f)),
            ("Color3.fromHex('FF8000')", Color3.FromRgb(255f, 128f, 0f)),
            ("Color3.fromHex('#f80')", Color3.FromRgb(255f, 136f, 0f)),
            ("c:Lerp(d, 0.25)", C.Lerp(D, 0.25f)),
            ("c:Lerp(d, 0)", C),
            ("c:Lerp(d, 1)", D),
            ("c.Lerp(c, d, 3)", C.Lerp(D, 3f)),
        ];

        Color3[] made = Colors([.. cases.Select(one => one.Luau)]);

        for (int i = 0; i < cases.Length; i++)
            made[i].ShouldBe(cases[i].Core, cases[i].Luau);
    }

    // Roblox's documentation shows three numbers for each and does not say
    // what one that is left out is. Chosen: 0, as Color3.new() is black there.
    [Fact]
    public void A_part_that_is_left_out_is_zero()
    {
        Colors("Color3.new()", "Color3.new(1)", "Color3.fromRGB(255)", "Color3.fromHSV()", "Color3.fromHSV(0, 0, nil)")
            .ShouldBe([default, new Color3(1f, 0f, 0f), new Color3(1f, 0f, 0f), default, default]);
    }

    [Fact]
    public void A_colour_gives_its_parts_its_hue_and_its_hex()
    {
        string[] printed = ScriptLevel.Run(Shared + """

            print(c.R, c.G, c.B)
            print(d:ToHSV())
            print(d:ToHex(), c:ToHex(), Color3.new(2, -1, 0.5):ToHex())
            print(Color3.fromHSV(0.6, 0.5, 0.75):ToHSV())
            """);

        (float h, float s, float v) = D.ToHsv();
        (float backH, float backS, float backV) = Color3.FromHsv(0.6f, 0.5f, 0.75f).ToHsv();
        Floats(printed[0]).ShouldBe([0.25f, 0.5f, 1f]);
        Floats(printed[1]).ShouldBe([h, s, v]);
        printed[2].ShouldBe("ff8000 4080ff ff0080");
        Floats(printed[3]).ShouldBe([backH, backS, backV]);
    }

    // The sample under ToHSV in Roblox's reference, which prints 1 1 1 for
    // the red and 0.3333333 1 1 for the green.
    [Fact]
    public void Red_and_green_give_the_hue_Robloxs_reference_prints_for_them()
    {
        string[] printed = ScriptLevel.Run("""
            print(Color3.fromRGB(255, 0, 0):ToHSV())
            print(Color3.fromRGB(0, 255, 0):ToHSV())
            print(Color3.fromHSV(1, 1, 1) == Color3.fromHSV(0, 1, 1))
            """);

        printed[0].ShouldBe("1 1 1");
        Floats(printed[1]).ShouldBe([1f / 3f, 1f, 1f]);
        printed[2].ShouldBe("true");
    }

    // Six digits for each part, as Roblox prints one.
    [Fact]
    public void A_colour_prints_as_Roblox_prints_one()
    {
        string[] printed = ScriptLevel.Run(Shared + """

            print(c)
            print(tostring(d), `{Color3.fromRGB(163, 162, 165)}`)
            print(typeof(c), type(c))
            """);

        printed.ShouldBe(
        [
            "0.25, 0.5, 1",
            "1, 0.501961, 0 0.639216, 0.635294, 0.647059",
            "Color3 userdata",
        ]);
    }

    [Fact]
    public void Two_colours_are_equal_when_their_parts_are_and_a_colour_is_a_table_key_by_itself_only()
    {
        string[] printed = ScriptLevel.Run(Shared + """

            print(c == Color3.new(0.25, 0.5, 1), c == d, c ~= d, d == Color3.fromHex('ff8000'))
            print(c == 1, c == 'c', c == Vector3.new(0.25, 0.5, 1), c == nil)
            local seen = { [c] = true }
            print(seen[c], seen[Color3.new(0.25, 0.5, 1)])
            """);

        printed.ShouldBe(["true false true true", "false false false false", "true nil"]);
    }

    [Fact]
    public void A_colour_takes_no_arithmetic_as_in_Roblox()
    {
        string[] printed = ScriptLevel.Run(Shared + """

            print(pcall(function() return c + d end))
            print(pcall(function() return c * 2 end))
            print(pcall(function() return -c end))
            print(pcall(function() return c < d end))
            """);

        printed.ShouldAllBe(line => line.StartsWith("false ", StringComparison.Ordinal));
        printed[0].ShouldContain("attempt to perform arithmetic (add) on Color3");
    }

    [Theory]
    [InlineData("c.Red", "Red is not a valid member of Color3")]
    [InlineData("c:Blend(d)", "Blend is not a valid member of Color3")]
    [InlineData("(function() c.R = 1 end)()", "R cannot be assigned to")]
    [InlineData("c.R()", "attempt to call a number value")]
    [InlineData("c:Lerp(d)", "invalid argument #2 to 'Lerp' (number expected, got nil)")]
    [InlineData("c:Lerp(Vector3.one, 0.5)", "invalid argument #1 to 'Lerp' (Color3 expected, got Vector3)")]
    [InlineData("c.Lerp(1, d, 0.5)", "Expected ':' not '.' calling member function Lerp")]
    [InlineData("Color3.new('red')", "invalid argument #1 to 'new' (number expected, got string)")]
    [InlineData("Color3.fromRGB(1, {}, 3)", "invalid argument #2 to 'fromRGB' (number expected, got table)")]
    [InlineData("Color3.fromHex(255)", "invalid argument #1 to 'fromHex' (string expected, got number)")]
    [InlineData(
        "Color3.fromHex('#ff80')",
        "Color3.fromHex takes six hex digits or three, with or without a # in front, and got \"#ff80\"")]
    [InlineData(
        "Color3.fromHex('not a colour')",
        "Color3.fromHex takes six hex digits or three, with or without a # in front, and got \"not a colour\"")]
    public void A_mistake_with_a_colour_is_an_error_that_says_what_it_was(string luau, string words)
    {
        string[] printed = ScriptLevel.Run($"{Shared}\nprint(select(2, pcall(function() return {luau} end)))");

        printed.ShouldHaveSingleItem().ShouldEndWith(words);
    }

    // The parts of each colour, as the floats the engine holds.
    private static Color3[] Colors(params string[] expressions)
    {
        string statements = string.Join(
            '\n', expressions.Select(expression => $"do local v = {expression} print(v.R, v.G, v.B) end"));
        string[] printed = ScriptLevel.Run($"{Shared}\n{statements}");

        printed.Length.ShouldBe(expressions.Length);
        return [.. printed.Select(Floats).Select(parts => new Color3(parts[0], parts[1], parts[2]))];
    }

    // Luau prints a number as the shortest text that reads back the same
    // double, and every double here began as a float.
    private static float[] Floats(string line) =>
        [.. line.Split(' ').Select(part => (float)double.Parse(part, CultureInfo.InvariantCulture))];
}
