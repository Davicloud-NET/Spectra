namespace SpectraEngine.Scripting.Tests;

// One script of the sample project, by the path a level would store for it.
// A mistake marks each wrong line: "--> words" is what both solvers have to
// say of it, "--> old: words" or "--> new: words" what one of them has to,
// and the other then has to say nothing of the line.
internal sealed record ScriptSample(string ContentPath, string Text)
{
    public const string Arrow = "-->";

    private const string OldOnly = "old:";
    private const string NewOnly = "new:";

    public bool IsMistake => ContentPath.StartsWith(ScriptSamples.MistakesFolder + "/", StringComparison.Ordinal);

    public string[] Lines => Text.TrimEnd('\n').Split('\n');

    // The script as a person would write it: no arrows.
    public string Code => string.Join('\n', Lines.Select(WithoutArrow)) + "\n";

    // The script with its comments taken out, to look for a name it uses.
    public string CodeWithoutComments => string.Join('\n', Lines.Select(WithoutComment));

    // Line numbers from 1, each with the words one solver has to say there.
    public Dictionary<int, string> Expected(bool newSolver)
    {
        var expected = new Dictionary<int, string>();
        string[] lines = Lines;
        for (int i = 0; i < lines.Length; i++)
        {
            if (WordsFor(lines[i], newSolver) is { } words)
                expected[i + 1] = words;
        }

        return expected;
    }

    // Every line that carries an arrow, whichever solver it is for.
    public int[] MarkedLines =>
        [.. Lines.Select((line, index) => (line, index)).Where(l => l.line.Contains(Arrow)).Select(l => l.index + 1)];

    private static string? WordsFor(string line, bool newSolver)
    {
        string[] parts = line.Split(Arrow);
        for (int i = 1; i < parts.Length; i++)
        {
            string words = parts[i].Trim();
            bool isOld = words.StartsWith(OldOnly, StringComparison.Ordinal);
            bool isNew = words.StartsWith(NewOnly, StringComparison.Ordinal);
            if (!isOld && !isNew)
                return words;
            if (isNew == newSolver)
                return words[OldOnly.Length..].Trim();
        }

        return null;
    }

    private static string WithoutArrow(string line)
    {
        int arrow = line.IndexOf(Arrow, StringComparison.Ordinal);
        return arrow < 0 ? line : line[..arrow].TrimEnd();
    }

    // No sample has "--" inside a string.
    private static string WithoutComment(string line)
    {
        int comment = line.IndexOf("--", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }
}
