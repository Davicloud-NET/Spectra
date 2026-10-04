namespace SpectraEngine.Editor.Shell;

/// <summary>
/// How well a typed query matches a command's name: a subsequence match, scored
/// so that word starts and runs beat scattered letters.
/// </summary>
public static class CommandScore
{
    /// <summary>A query that matched nothing.</summary>
    public const int NoMatch = -1;

    /// <summary>
    /// Scores <paramref name="candidate"/> against <paramref name="query"/>, or
    /// <see cref="NoMatch"/>. Higher is better; ties are the caller's to break.
    /// </summary>
    public static int Of(string candidate, string query)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Length == 0)
        {
            return 0;
        }

        int score = 0;
        int at = 0;
        int run = 0;

        foreach (char wanted in query)
        {
            if (char.IsWhiteSpace(wanted))
            {
                continue;
            }

            // Prefer a word start over the nearest letter: "fe" should take the
            // e of "everything" in "Frame everything", not the e of "Frame".
            int found = IndexOf(candidate, wanted, at, wordStartOnly: true);
            if (found < 0)
            {
                found = IndexOf(candidate, wanted, at, wordStartOnly: false);
            }

            if (found < 0)
            {
                return NoMatch;
            }

            bool startsWord = StartsWord(candidate, found);
            run = found == at ? run + 1 : 0;

            score += 10;
            score += startsWord ? 18 : 0;
            score += run * 6;
            score -= Math.Min(found - at, 6);

            at = found + 1;
        }

        // Shorter names win ties.
        return score - (candidate.Length / 8);
    }

    private static int IndexOf(string text, char wanted, int from, bool wordStartOnly)
    {
        for (int i = from; i < text.Length; i++)
        {
            if (char.ToUpperInvariant(text[i]) != char.ToUpperInvariant(wanted))
            {
                continue;
            }

            if (!wordStartOnly || StartsWord(text, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool StartsWord(string text, int at) =>
        at == 0 || text[at - 1] is ' ' or ':' or '-';
}
