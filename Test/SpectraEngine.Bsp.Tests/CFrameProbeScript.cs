using System.Text;

namespace SpectraEngine.Bsp.Tests;

// The part of Data/cframe-probe.luau that is written from CFrameProbeCases:
// one line per case, between two marks.
internal static class CFrameProbeScript
{
    public const string CasesMark = "--[[ cases ]]";
    public const string EndMark = "--[[ end of cases ]]";

    // One Luau line per case, in order, with line feeds between them.
    public static string CaseLines(IReadOnlyList<CFrameProbeCase> cases)
    {
        var lines = new StringBuilder();
        foreach (CFrameProbeCase probeCase in cases)
        {
            if (lines.Length > 0)
                lines.Append('\n');
            lines.Append($"{probeCase.Reader}(\"{probeCase.Name}\", function() return {probeCase.Luau} end)");
        }

        return lines.ToString();
    }

    // The lines between the two marks of a probe script.
    public static string CasesIn(string script)
    {
        (int start, int end) = Block(script);
        return script[start..end].ReplaceLineEndings("\n").Trim('\n');
    }

    // The script with the lines between its two marks replaced.
    public static string WithCases(string script, string caseLines)
    {
        (int start, int end) = Block(script);
        return script[..start] + "\n" + caseLines + "\n" + script[end..];
    }

    private static (int Start, int End) Block(string script)
    {
        int mark = script.IndexOf(CasesMark, StringComparison.Ordinal);
        int end = script.IndexOf(EndMark, StringComparison.Ordinal);
        if (mark < 0 || end < mark)
            throw new InvalidOperationException($"The probe script has lost its '{CasesMark}' and '{EndMark}' marks.");

        return (mark + CasesMark.Length, end);
    }
}
