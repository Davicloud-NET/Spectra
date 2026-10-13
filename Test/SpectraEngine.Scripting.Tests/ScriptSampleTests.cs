using System.Text;
using System.Text.RegularExpressions;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The sample scripts held against the script API's description, with no
/// luau-lsp: a class or a global nobody uses in a sample is one the checker
/// was never asked about.
/// </summary>
public sealed class ScriptSampleTests
{
    private static ScriptApiDescription Api => LuauScriptApi.Description;

    public static TheoryData<string> Samples() => [.. ScriptSamples.All.Select(sample => sample.ContentPath)];

    [Fact]
    public void Every_class_of_the_description_is_named_in_a_clean_sample()
    {
        string[] unused = [.. Api.Classes.Select(described => described.Name).Where(name => !IsUsedCleanly(name))];

        unused.ShouldBeEmpty(
            "No clean sample under Test/SpectraEngine.Scripting.Tests/Samples names these classes. Give " +
            "each a line that uses it, so luau-lsp checks what the definitions file says of it.");
    }

    [Fact]
    public void Every_library_and_global_of_the_description_is_used_in_a_clean_sample()
    {
        IEnumerable<string> names = Api.Libraries.Select(library => library.Name)
            .Concat(Api.Globals.Select(global => global.Name))
            .Concat(Api.Functions.Where(function => function.Deprecated.Length == 0).Select(function => function.Name));

        string[] unused = [.. names.Where(name => !IsUsedCleanly(name))];

        unused.ShouldBeEmpty(
            "No clean sample under Test/SpectraEngine.Scripting.Tests/Samples uses these. Give each a line.");
    }

    // A clean sample cannot call one: the checker says of every call that the
    // function is old. So its sample is a mistake that says so.
    [Fact]
    public void Every_old_global_is_called_in_a_mistake_that_names_what_to_use()
    {
        ScriptFunctionDescription[] old = [.. Api.Functions.Where(function => function.Deprecated.Length > 0)];
        old.ShouldNotBeEmpty();

        foreach (ScriptFunctionDescription function in old)
        {
            string call = function.Name + "(";
            string words = $"Function '{function.Name}' is deprecated, use '{function.Deprecated}' instead";

            ScriptSamples.Mistakes
                .SelectMany(sample => sample.Lines)
                .ShouldContain(
                    line => line.StartsWith(call, StringComparison.Ordinal) && line.EndsWith(words, StringComparison.Ordinal),
                    $"no mistake calls {function.Name} on a line marked \"{ScriptSample.Arrow} {words}\"");
        }
    }

    [Fact]
    public void A_clean_sample_marks_no_line_and_a_mistake_marks_at_least_one()
    {
        foreach (ScriptSample sample in ScriptSamples.Clean)
            sample.MarkedLines.ShouldBeEmpty($"{sample.ContentPath} is no mistake and has an arrow");

        ScriptSamples.Mistakes.ShouldNotBeEmpty();
        foreach (ScriptSample sample in ScriptSamples.Mistakes)
            sample.MarkedLines.ShouldNotBeEmpty($"{sample.ContentPath} is a mistake and marks no line");
    }

    [Fact]
    public void A_mark_for_one_solver_leaves_the_other_with_nothing_to_say()
    {
        var sample = new ScriptSample("Mistakes/x.luau", """
            local a = 1
            a.b = 2 --> both say this
            a.c = 3 --> new: read-only
            print(a + a) --> old: 'Vector3' --> new: Operator '+'
            """.ReplaceLineEndings("\n"));

        sample.Expected(newSolver: false).ShouldBe(
            new Dictionary<int, string> { [2] = "both say this", [4] = "'Vector3'" });
        sample.Expected(newSolver: true).ShouldBe(
            new Dictionary<int, string> { [2] = "both say this", [3] = "read-only", [4] = "Operator '+'" });
        sample.MarkedLines.ShouldBe([2, 3, 4]);
        sample.Code.ShouldBe("local a = 1\na.b = 2\na.c = 3\nprint(a + a)\n");
    }

    [Fact]
    public void A_message_of_several_lines_is_read_as_one()
    {
        LuauLspDiagnostic[] said = LuauLsp.Parse("""
            [INFO] Loading definitions file: @spectra - spectra.d.luau
            Assets/Scripts/a.luau(8,30): TypeError: Expected this to be
            	'(number) -> ()'
            but got
            	'(string) -> ()'
            Assets\Scripts\a.luau(26,1): DeprecatedApi: Function 'wait' is deprecated, use 'task.wait' instead
            [INFO] done
            not a message
            """);

        said.Length.ShouldBe(2);
        said[0].Line.ShouldBe(8);
        said[0].Text.ShouldContain("'(number) -> ()'");
        said[0].Text.ShouldEndWith("'(string) -> ()'");
        said[1].ShouldBe(new LuauLspDiagnostic(
            "Assets/Scripts/a.luau", 26, 1, "DeprecatedApi", "Function 'wait' is deprecated, use 'task.wait' instead"));
    }

    // luau-lsp has a Luau of its own. What it passes, the engine's has to compile.
    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_sample_compiles_with_the_engines_own_luau(string contentPath)
    {
        NativeLuau.Require();
        ScriptSample sample = ScriptSamples.At(contentPath);

        new LuauSyntaxCheck().Check(Encoding.UTF8.GetBytes(sample.Text)).ShouldBeNull();
    }

    private static bool IsUsedCleanly(string name)
    {
        var word = new Regex($@"\b{Regex.Escape(name)}\b");
        return ScriptSamples.Clean.Any(sample => word.IsMatch(sample.CodeWithoutComments));
    }
}
