using System.Globalization;
using System.Text;

namespace SpectraEngine.Core.Entities;

// How the entity commands and the watch spell a name, an input and a count.
// One place, so a grep written against one command's output finds the others'.
internal static class EntityConsoleText
{
    // Follows a command's name and a colon, so it starts in lower case.
    public const string LevelReplaced =
        "play mode is on but the level was replaced. Leave play mode and enter it again.";

    // Quoted when a reader could not tell where the name ends: it is empty, or
    // holds a space, a dot or a quote. The escapes are the console's own, so a
    // quoted name can be typed back.
    public static StringBuilder AppendName(StringBuilder text, string name)
    {
        if (!NeedsQuotes(name))
            return text.Append(name);

        return AppendQuoted(text, name);
    }

    public static string Name(string name) =>
        NeedsQuotes(name) ? AppendQuoted(new StringBuilder(name.Length + 2), name).ToString() : name;

    // Open, or Add("5") when the input carries an argument.
    public static StringBuilder AppendCall(StringBuilder text, string input, string parameter)
    {
        AppendName(text, input);
        if (parameter.Length == 0)
            return text;

        text.Append('(');
        return AppendQuoted(text, parameter).Append(')');
    }

    // Rounded to the millisecond: a delay read back off two world times is
    // off by a float's worth.
    public static string Seconds(float seconds) =>
        seconds.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Count(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");

    // "1 entity named counter", "3 entities matching door*".
    public static string Reach(int count, string target) =>
        $"{Count(count, "entity", "entities")} " +
        $"{(TargetNamePattern.IsPrefix(target) ? "matching" : "named")} {Name(target)}";

    public static string NothingAnswers(string target) =>
        TargetNamePattern.IsPrefix(target)
            ? $"nothing matches {Name(target)}"
            : $"nothing is named {Name(target)}";

    public static bool MatchesNameOrClass(string pattern, string name, string className) =>
        TargetNamePattern.Matches(pattern, name) || TargetNamePattern.Matches(pattern, className);

    private static bool NeedsQuotes(string name)
    {
        if (name.Length == 0)
            return true;

        foreach (char c in name)
        {
            if (char.IsWhiteSpace(c) || c is '.' or '"')
                return true;
        }

        return false;
    }

    private static StringBuilder AppendQuoted(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (char c in value)
        {
            if (c is '"' or '\\')
                text.Append('\\');
            text.Append(c);
        }

        return text.Append('"');
    }
}
