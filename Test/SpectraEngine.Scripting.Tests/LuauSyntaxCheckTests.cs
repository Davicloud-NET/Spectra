using System.Text;
using System.Text.RegularExpressions;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The check a cook asks before it packs a script: the same verdict a level
/// gets when it loads the script, with no Luau state behind it.
/// </summary>
public sealed partial class LuauSyntaxCheckTests
{
    private readonly LuauSyntaxCheck _check = new();

    // A mistake on a known line each, and scripts that have none.
    private static readonly string[] Samples =
    [
        "",
        "print('hello')",
        "-- only a comment\n",
        "local door: number = 1\ndoor += 1\nreturn `it is {door}`",
        "local at = Vector3.new(1, 2, 3)\nreturn at.X",
        "for i = 1, 3 do\n    if i == 2 then continue end\nend",
        "local door = 1\nlocal = 5",
        "return return",
        "print('one')\nprint('two')\nif then\nend",
        "local a = (1 + \n",
        "while true do",
        "x = = 1",
    ];

    public LuauSyntaxCheckTests() => NativeLuau.Require();

    [Fact]
    public void A_script_that_compiles_has_no_mistake()
    {
        Check("local speed = 20\nreturn speed * 2").ShouldBeNull();
        Check(string.Empty).ShouldBeNull();
    }

    [Fact]
    public void A_mistake_is_named_by_its_line_and_in_Luaus_words()
    {
        ScriptSyntaxError mistake = Check("local door = 1\nlocal = 5").ShouldNotBeNull();

        mistake.Line.ShouldBe(2);
        mistake.Message.ShouldStartWith("Expected identifier");

        // Luau's compiler hands over a line and no column.
        mistake.Column.ShouldBe(0);
    }

    [Fact]
    public void The_first_mistake_of_several_is_the_one_named()
    {
        Check("print(1)\nlocal = 1\nlocal = 2\n").ShouldNotBeNull().Line.ShouldBe(2);
    }

    [Fact]
    public void A_byte_order_mark_is_not_a_mistake_and_moves_no_line()
    {
        byte[] marked = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("print(1)\nlocal = 5")];
        byte[] fine = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("print(1)")];

        _check.Check(fine).ShouldBeNull();
        _check.Check(marked).ShouldNotBeNull().Line.ShouldBe(2);
    }

    [Fact]
    public void A_packed_script_is_not_text_that_compiles()
    {
        byte[] packed = [0x1B, (byte)'S', (byte)'L', (byte)'U', 1, 0, 1, 0, 8, 0, 0, 0, 0, 0, 0, 0, .. "print(1)"u8];

        _check.Check(packed).ShouldNotBeNull().Line.ShouldBe(1);
    }

    [Fact]
    public void The_check_and_a_level_that_loads_the_script_say_the_same()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);

        foreach (string sample in Samples)
        {
            sink.Messages.Clear();
            bool loads = host.Load(TestHost.ScriptName, Encoding.UTF8.GetBytes(sample)) is not null;
            ScriptSyntaxError? mistake = Check(sample);

            (mistake is null).ShouldBe(loads, sample);
            if (mistake is not { } found)
                continue;

            ScriptMessage reported = sink.Single;
            found.Line.ShouldBe(reported.Line, sample);
            found.Message.ShouldBe(reported.Text, sample);
        }
    }

    [Fact]
    public void Many_threads_may_ask_at_once()
    {
        var lines = new int[256];

        Parallel.For(0, lines.Length, i =>
        {
            // A mistake on line i + 1 for every other caller.
            string text = string.Concat(Enumerable.Repeat("print(1)\n", i)) + (i % 2 == 0 ? "local = 5" : "print(2)");
            lines[i] = Check(text)?.Line ?? 0;
        });

        for (int i = 0; i < lines.Length; i++)
            lines[i].ShouldBe(i % 2 == 0 ? i + 1 : 0, $"caller {i}");
    }

    [Fact]
    public void It_names_the_release_the_binding_was_written_for()
    {
        _check.CompilerVersion.ShouldStartWith($"luau 0.{Lua.Release},");
        _check.Problem.ShouldBeNull();
    }

    [Fact]
    public void Its_version_names_the_shim_and_the_compile_options_too()
    {
        // A cook keeps a verdict under this string. The shim sets the Luau
        // flags a script compiles under, and the options are the other half.
        _check.CompilerVersion.ShouldBe($"luau 0.{Lua.Release}, shim {Shim.Revision}, O1 g1");
    }

    [Fact]
    public void The_check_compiles_with_the_options_a_level_loads_with()
    {
        string binding = Path.Combine(BindingSourceTests.RepositoryRoot(), BindingSourceTests.Binding);
        string loader = File.ReadAllText(Path.Combine(binding, "Native", "LuaChunk.cs"));
        string check = File.ReadAllText(Path.Combine(binding, "LuauSyntaxCheck.cs"));

        string[] ofLoader = [.. Option().Matches(loader).Select(match => match.Value)];
        string[] ofCheck = [.. Option().Matches(check).Select(match => match.Value)];

        ofLoader.Length.ShouldBe(4, "LuaChunk no longer states its four compile options the way this test reads them");
        ofCheck.ShouldBe(ofLoader);
    }

    // The two levels and the two names of the vector type, as each file states them.
    [GeneratedRegex(@"(?:OptimizationLevel|DebugLevel) = \d+;|Vector(?:Library|Constructor) => ""[^""]*""u8;")]
    private static partial Regex Option();

    private ScriptSyntaxError? Check(string text) => _check.Check(Encoding.UTF8.GetBytes(text));
}
