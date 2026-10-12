using System.Collections.Generic;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Reads what an attribute says as Luau writes it: a list of parameters such
// as "goal: CFrame, alpha: number?", and a type. Only far enough to split a
// list and to catch a slip. Luau itself is the judge of a type.
internal static class LuauSignatureText
{
    // Null when the text reads. Otherwise what is wrong with it.
    public static string? ReadParameters(string text, List<LuauParameterModel> parameters)
    {
        if (WhyNotBalanced(text) is { } unbalanced)
            return unbalanced;

        foreach (string piece in SplitAtCommas(text))
        {
            int colon = TopLevelColon(piece);
            if (colon < 0)
                return $"'{piece.Trim()}' has no name and colon in front of its type";

            string name = piece.Substring(0, colon).Trim();
            string type = piece.Substring(colon + 1).Trim();
            if (name != "..." && !IsName(name))
                return $"'{name}' is no name for a parameter";
            if (type.Length == 0)
                return $"'{name}' has no type";

            parameters.Add(new LuauParameterModel(name, type));
        }

        return null;
    }

    public static string? WhyNotBalanced(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // The arrow of a function type is no closing bracket.
            if (c == '-' && i + 1 < text.Length && text[i + 1] == '>')
            {
                i++;
                continue;
            }

            if (c is '(' or '{' or '<' or '[')
                depth++;
            else if (c is ')' or '}' or '>' or ']')
                depth--;

            if (depth < 0)
                return "a bracket closes that was never opened";
        }

        return depth == 0 ? null : "a bracket is never closed";
    }

    private static List<string> SplitAtCommas(string text)
    {
        var pieces = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '-' && i + 1 < text.Length && text[i + 1] == '>')
                i++;
            else if (c is '(' or '{' or '<' or '[')
                depth++;
            else if (c is ')' or '}' or '>' or ']')
                depth--;
            else if (c == ',' && depth == 0)
            {
                pieces.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (text.Substring(start).Trim().Length > 0 || pieces.Count > 0)
            pieces.Add(text.Substring(start));

        return pieces;
    }

    private static int TopLevelColon(string piece)
    {
        int depth = 0;
        for (int i = 0; i < piece.Length; i++)
        {
            char c = piece[i];
            if (c is '(' or '{' or '<' or '[')
                depth++;
            else if (c is ')' or '}' or '>' or ']')
                depth--;
            else if (c == ':' && depth == 0)
                return i;
        }

        return -1;
    }

    public static bool IsName(string text)
    {
        if (text.Length == 0 || char.IsDigit(text[0]))
            return false;

        foreach (char c in text)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }
}
