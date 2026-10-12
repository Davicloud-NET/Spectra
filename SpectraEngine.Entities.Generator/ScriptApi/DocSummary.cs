using Microsoft.CodeAnalysis;
using System.Text;
using System.Threading;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// The <summary> of a declaration as one line of plain text. It is read from
// the /// lines themselves: a build that writes no documentation file hands
// the generator no documentation, only comments.
internal static class DocSummary
{
    public static string Of(ISymbol symbol, CancellationToken token)
    {
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            string summary = FromComment(reference.GetSyntax(token).GetLeadingTrivia().ToFullString());
            if (summary.Length > 0)
                return summary;
        }

        return "";
    }

    internal static string FromComment(string trivia)
    {
        var xml = new StringBuilder();
        foreach (string line in trivia.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("///", System.StringComparison.Ordinal))
                xml.Append(trimmed.Substring(3)).Append(' ');
        }

        string text = xml.ToString();
        int start = text.IndexOf("<summary>", System.StringComparison.Ordinal);
        int end = text.IndexOf("</summary>", System.StringComparison.Ordinal);
        if (start < 0 || end < start)
            return "";

        return Plain(text.Substring(start + 9, end - start - 9));
    }

    // Tags go, and what they name stays: <see cref="Scene.Root"/> reads as Root.
    private static string Plain(string xml)
    {
        var plain = new StringBuilder(xml.Length);
        int at = 0;
        while (at < xml.Length)
        {
            int open = xml.IndexOf('<', at);
            if (open < 0)
                break;

            int close = xml.IndexOf('>', open);
            if (close < 0)
                break;

            plain.Append(xml, at, open - at);
            plain.Append(NamedBy(xml.Substring(open + 1, close - open - 1)));
            at = close + 1;
        }

        plain.Append(xml, at, xml.Length - at);
        return Collapse(plain.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&"));
    }

    // What a tag such as: see cref="X.Y" / stands for in the sentence.
    private static string NamedBy(string tag)
    {
        if (!tag.EndsWith("/", System.StringComparison.Ordinal))
            return "";

        int quote = tag.IndexOf('"');
        int last = tag.LastIndexOf('"');
        if (quote < 0 || last <= quote)
            return "";

        string name = tag.Substring(quote + 1, last - quote - 1);
        int brace = name.IndexOfAny(new[] { '(', '{' });
        if (brace >= 0)
            name = name.Substring(0, brace);

        return name.Substring(name.LastIndexOfAny(new[] { '.', ':' }) + 1);
    }

    private static string Collapse(StringBuilder text)
    {
        var collapsed = new StringBuilder(text.Length);
        bool wasSpace = true;
        for (int i = 0; i < text.Length; i++)
        {
            bool isSpace = char.IsWhiteSpace(text[i]);
            if (!isSpace)
                collapsed.Append(text[i]);
            else if (!wasSpace)
                collapsed.Append(' ');

            wasSpace = isSpace;
        }

        return collapsed.ToString().TrimEnd();
    }
}
