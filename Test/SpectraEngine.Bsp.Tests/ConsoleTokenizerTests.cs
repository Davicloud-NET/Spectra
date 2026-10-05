using SpectraEngine.Core.ConsoleSystem;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>How a console line splits into commands and a command into tokens.</summary>
// Rows write ' for a double quote, in the line and in what is expected, so the
// escapes stay readable. A token shows as [text] and commands are joined by " | ".
public sealed class ConsoleTokenizerTests
{
    [Theory]
    [InlineData("echo a; echo b", "[echo][a] | [echo][b]")]
    [InlineData("echo a;echo b", "[echo][a] | [echo][b]")]
    [InlineData("echo 'a; b'; echo c", "[echo][a; b] | [echo][c]")]
    [InlineData(@"echo 'a\';b'; echo c", "[echo][a';b] | [echo][c]")]
    public void A_semicolon_inside_quotes_does_not_split(string line, string expected) =>
        Split(line).ShouldBe((Quotes(expected), false));

    [Theory]
    [InlineData("echo a // echo b; echo c", "[echo][a]")]
    [InlineData("echo a; // echo b", "[echo][a]")]
    [InlineData("echo 'a // b' c", "[echo][a // b][c]")]
    [InlineData("echo 'http://host' // the address", "[echo][http://host]")]
    public void A_comment_marker_inside_quotes_is_text(string line, string expected) =>
        Split(line).ShouldBe((Quotes(expected), false));

    [Theory]
    [InlineData(@"echo 'a\'b'", "[echo][a'b]")]
    [InlineData(@"echo 'a\\b'", @"[echo][a\b]")]
    [InlineData(@"echo 'a\\'", @"[echo][a\]")]
    [InlineData(@"echo 'a\nb'", @"[echo][a\nb]")]
    [InlineData(@"echo a\b", @"[echo][a\b]")]
    public void Only_a_quote_and_a_backslash_can_be_escaped(string line, string expected) =>
        Split(line).ShouldBe((Quotes(expected), false));

    [Theory]
    [InlineData("echo 'a b", "[echo][a b]")]
    [InlineData("echo 'a; echo b", "[echo][a; echo b]")]
    [InlineData(@"echo 'a\'", "[echo][a']")]
    [InlineData("echo a; echo '", "[echo][a] | [echo][]")]
    public void An_unterminated_quote_closes_at_the_end_and_warns(string line, string expected) =>
        Split(line).ShouldBe((Quotes(expected), true));

    [Theory]
    [InlineData("echo '' 2", "[echo][][2]")]
    [InlineData("echo a ''", "[echo][a][]")]
    [InlineData("''", "[]")]
    public void An_empty_quoted_token_is_an_argument(string line, string expected) =>
        Split(line).ShouldBe((Quotes(expected), false));

    [Theory]
    [InlineData("echo a'b c'd", "[echo][a][b c][d]")]
    [InlineData("echo 'a''b'", "[echo][a][b]")]
    public void A_quote_starts_a_new_token_wherever_it_stands(string line, string expected) =>
        Split(line).ShouldBe((Quotes(expected), false));

    [Theory]
    [InlineData("  echo \t a   b  ", "[echo][a][b]")]
    [InlineData("echo ' a  b '", "[echo][ a  b ]")]
    public void Whitespace_separates_tokens_and_survives_inside_quotes(string line, string expected) =>
        Split(line).ShouldBe((Quotes(expected), false));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";; ;")]
    [InlineData("// nothing here")]
    public void A_line_with_no_command_yields_none(string line) =>
        Split(line).ShouldBe(("", false));

    [Fact]
    public void Empty_commands_between_semicolons_are_skipped() =>
        Split("; echo a;; echo b ;").ShouldBe(("[echo][a] | [echo][b]", false));

    private static string Quotes(string row) => row.Replace('\'', '"');

    private static (string Commands, bool QuoteLeftOpen) Split(string row)
    {
        string line = Quotes(row);
        var text = new char[line.Length];
        var tokens = new Range[line.Length];
        var commands = new StringBuilder();
        bool anyQuoteLeftOpen = false;

        int position = 0;
        while (ConsoleTokenizer.TryNextCommand(line, ref position, out ReadOnlySpan<char> command))
        {
            int count = ConsoleTokenizer.Tokenize(command, text, tokens, out _, out bool quoteLeftOpen);
            anyQuoteLeftOpen |= quoteLeftOpen;

            if (commands.Length > 0)
                commands.Append(" | ");
            for (int i = 0; i < count; i++)
                commands.Append('[').Append(text.AsSpan(tokens[i])).Append(']');
        }

        return (commands.ToString(), anyQuoteLeftOpen);
    }
}
