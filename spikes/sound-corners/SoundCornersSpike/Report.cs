using System;
using System.Collections.Generic;
using System.Text;

namespace SoundCornersSpike;

// Prints Markdown tables, so a log can be pasted into the report.
internal static class Report
{
    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"## {title}");
        Console.WriteLine();
    }

    public static void Note(string text) => Console.WriteLine(text);

    public static void Table(IReadOnlyList<string> header, IReadOnlyList<string[]> rows)
    {
        var widths = new int[header.Count];
        for (int c = 0; c < header.Count; c++) widths[c] = header[c].Length;

        foreach (string[] row in rows)
        {
            for (int c = 0; c < row.Length && c < widths.Length; c++)
                widths[c] = Math.Max(widths[c], row[c].Length);
        }

        Console.WriteLine();
        Console.WriteLine(Line(header, widths));

        var rule = new string[header.Count];
        for (int c = 0; c < rule.Length; c++) rule[c] = new string('-', widths[c]);
        Console.WriteLine(Line(rule, widths));

        foreach (string[] row in rows) Console.WriteLine(Line(row, widths));
        Console.WriteLine();
    }

    public static string F(double value, int digits = 2) => value.ToString("F" + digits);

    public static string Percent(double fraction, int digits = 1) => (fraction * 100.0).ToString("F" + digits) + "%";

    private static string Line(IReadOnlyList<string> cells, int[] widths)
    {
        var line = new StringBuilder("|");
        for (int c = 0; c < widths.Length; c++)
        {
            string cell = c < cells.Count ? cells[c] : "";
            line.Append(' ').Append(cell.PadRight(widths[c])).Append(" |");
        }

        return line.ToString();
    }
}
